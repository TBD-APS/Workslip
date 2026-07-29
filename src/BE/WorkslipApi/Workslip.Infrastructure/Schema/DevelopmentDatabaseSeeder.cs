using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

public sealed class DevelopmentDatabaseSeeder(
    SqlDbContext db,
    ISuperadminEntraService entraService,
    ILogger<DevelopmentDatabaseSeeder> logger)
{
    private static readonly Guid CanonicalRasmusSuperadminId =
        new("92779E5B-DA5B-4CC4-BBEB-07B40CAB806F");

    private static readonly Guid CanonicalMahadSuperadminId =
        new("D4D4D4D4-DA5B-4CC4-BBEB-07B40CAB806F");

    private const string CanonicalMahadEmail = "mahad8@outlook.dk";
    private const string CanonicalMahadDisplayName = "Mahad";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await DatabaseSeeder.Seed(db);

        db.IsSeeding = true;
        try
        {
            var rasmus = await GetRequiredRasmusSuperadminAsync(cancellationToken);
            await ReconcileSuperadminAsync(rasmus, rasmus.OrganizationId, cancellationToken);

            var mahad = await GetOrCreateMahadSuperadminAsync(
                rasmus.OrganizationId,
                cancellationToken);
            await ReconcileSuperadminAsync(mahad, rasmus.OrganizationId, cancellationToken);
        }
        finally
        {
            db.IsSeeding = false;
        }
    }

    private async Task<UserDataRow> GetRequiredRasmusSuperadminAsync(
        CancellationToken cancellationToken)
    {
        var user = await db.Users
            .SingleOrDefaultAsync(
                candidate => candidate.Id == CanonicalRasmusSuperadminId,
                cancellationToken);

        return user ?? throw new InvalidOperationException(
            $"Canonical development superadmin '{CanonicalRasmusSuperadminId}' was not created by DatabaseSeeder. Resolve any conflicting seeded email before startup.");
    }

    private async Task<UserDataRow> GetOrCreateMahadSuperadminAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = CanonicalMahadEmail.ToLowerInvariant();
        var matchingUsers = await db.Users
            .Where(user =>
                user.Id == CanonicalMahadSuperadminId ||
                user.Email.ToLower() == normalizedEmail)
            .ToListAsync(cancellationToken);

        if (matchingUsers.Count > 1)
        {
            throw new InvalidOperationException(
                $"Development superadmin identity conflict: ID '{CanonicalMahadSuperadminId}' and email '{normalizedEmail}' belong to different Workslip users.");
        }

        if (matchingUsers.Count == 1)
        {
            var existing = matchingUsers[0];
            if (existing.Id != CanonicalMahadSuperadminId ||
                !string.Equals(existing.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Development superadmin identity conflict: '{normalizedEmail}' does not belong to canonical user '{CanonicalMahadSuperadminId}'.");
            }

            return existing;
        }

        var timestamp = DateTimeOffset.UtcNow;
        var mahad = new UserDataRow
        {
            Id = CanonicalMahadSuperadminId,
            OrganizationId = organizationId,
            DisplayName = CanonicalMahadDisplayName,
            Email = CanonicalMahadEmail,
            Phone = string.Empty,
            Role = Roles.Superadmin,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };

        db.Users.Add(mahad);
        return mahad;
    }

    private async Task ReconcileSuperadminAsync(
        UserDataRow user,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = user.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            throw new InvalidOperationException(
                $"Canonical development superadmin '{user.Id}' has no email address.");
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
            user.OrganizationId = organizationId;
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
            "Development organization-bound Superadmin reconciled. UserId: {UserId}. OrganizationId: {OrganizationId}. EntraUserId: {EntraUserId}. EntraIdentityCreated: {EntraIdentityCreated}.",
            user.Id,
            organizationId,
            entraUser.EntraUserId,
            entraUser.Created);
    }
}
