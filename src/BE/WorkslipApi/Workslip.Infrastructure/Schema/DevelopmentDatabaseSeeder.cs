using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Workslip.Application.Users;
using Workslip.Domain;

namespace Workslip.Infrastructure.Schema;

public sealed class DevelopmentDatabaseSeeder(
    SqlDbContext db,
    ISuperadminEntraService entraService,
    ILogger<DevelopmentDatabaseSeeder> logger)
{
    private static readonly Guid CanonicalSuperadminId =
        new("92779E5B-DA5B-4CC4-BBEB-07B40CAB806F");

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await DatabaseSeeder.Seed(db);

        db.IsSeeding = true;
        try
        {
            await ReconcileSuperadminAsync(cancellationToken);
        }
        finally
        {
            db.IsSeeding = false;
        }
    }

    private async Task ReconcileSuperadminAsync(CancellationToken cancellationToken)
    {
        var user = await db.Users
            .SingleOrDefaultAsync(candidate => candidate.Id == CanonicalSuperadminId, cancellationToken);

        if (user is null)
        {
            throw new InvalidOperationException(
                $"Canonical development superadmin '{CanonicalSuperadminId}' was not created by DatabaseSeeder. Resolve any conflicting seeded email before startup.");
        }

        var normalizedEmail = user.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new InvalidOperationException(
                $"Canonical development superadmin '{CanonicalSuperadminId}' has no email address.");
        }

        var emailOwnedByAnotherUser = await db.Users.AnyAsync(
            candidate =>
                candidate.Id != user.Id &&
                candidate.Email.ToLower() == normalizedEmail,
            cancellationToken);
        if (emailOwnedByAnotherUser)
        {
            throw new InvalidOperationException(
                $"Development superadmin email '{normalizedEmail}' belongs to multiple Workslip users.");
        }

        var entraUser = await entraService.EnsureSuperadminAsync(
            normalizedEmail,
            user.DisplayName,
            cancellationToken);

        try
        {
            user.Email = normalizedEmail;
            user.Role = Roles.Superadmin;
            user.EntraId = entraUser.EntraUserId;
            user.EntraEmail = entraUser.EntraMail;
            user.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception persistenceException) when (entraUser.Created)
        {
            try
            {
                await entraService.DeleteUserAsync(entraUser.EntraUserId, CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                logger.LogError(
                    rollbackException,
                    "Development superadmin Entra rollback failed. EntraUserId: {EntraUserId}.",
                    entraUser.EntraUserId);

                throw new AggregateException(
                    "Development superadmin persistence and Entra rollback both failed.",
                    persistenceException,
                    rollbackException);
            }

            throw;
        }

        logger.LogInformation(
            "Development superadmin reconciled. UserId: {UserId}. EntraUserId: {EntraUserId}. EntraIdentityCreated: {EntraIdentityCreated}.",
            user.Id,
            entraUser.EntraUserId,
            entraUser.Created);
    }
}
