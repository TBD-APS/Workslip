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

    private const string CanonicalSuperadminEmail = "rasmusvm6@hotmail.com";

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
        var normalizedEmail = CanonicalSuperadminEmail.ToLowerInvariant();
        var matchingUsers = await db.Users
            .Where(user =>
                user.Id == CanonicalSuperadminId ||
                user.Email.ToLower() == normalizedEmail)
            .ToListAsync(cancellationToken);

        if (matchingUsers.Count == 0)
        {
            throw new InvalidOperationException(
                $"Development superadmin '{normalizedEmail}' was not created by DatabaseSeeder.");
        }

        if (matchingUsers.Count > 1)
        {
            throw new InvalidOperationException(
                $"Development superadmin identity conflict: ID '{CanonicalSuperadminId}' and email '{normalizedEmail}' belong to different users.");
        }

        var user = matchingUsers[0];
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
