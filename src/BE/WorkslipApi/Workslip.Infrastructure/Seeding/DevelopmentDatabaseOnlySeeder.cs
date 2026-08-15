using Microsoft.EntityFrameworkCore;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

public static class DevelopmentDatabaseOnlySeeder
{
    internal static readonly Guid LocalSuperadminId =
        new("F6F6F6F6-DA5B-4CC4-BBEB-07B40CAB806F");

    internal const string LocalSuperadminEmail = "superadmin@17v3ygzs.mailosaur.net";
    private const string LocalSuperadminDisplayName = "Local Superadmin";

    public static async Task SeedAsync(
        SqlDbContext db,
        InstallationBaselineProvisioner installationBaselineProvisioner,
        CancellationToken cancellationToken = default)
    {
        await EnsureLocalSuperadminAsync(db, cancellationToken);
        await DatabaseSeeder.Seed(db, installationBaselineProvisioner, cancellationToken);
        await DevelopmentTestUserAudienceReconciler.ReconcileAsync(db, cancellationToken);
    }

    private static async Task EnsureLocalSuperadminAsync(
        SqlDbContext db,
        CancellationToken cancellationToken)
    {
        var platformMatches = await db.Organizations
            .Where(organization =>
                organization.Id == PlatformOrganization.Id ||
                organization.Cvr == PlatformOrganization.Cvr)
            .ToListAsync(cancellationToken);

        if (platformMatches.Count > 1 ||
            (platformMatches.Count == 1 &&
             (platformMatches[0].Id != PlatformOrganization.Id ||
              platformMatches[0].Cvr != PlatformOrganization.Cvr)))
        {
            throw new InvalidOperationException(
                "Reserved platform Organization identity is inconsistent; local Superadmin seed refused to continue.");
        }

        var normalizedEmail = LocalSuperadminEmail.ToLowerInvariant();
        var userMatches = await db.Users
            .Where(user =>
                user.Id == LocalSuperadminId ||
                (user.Email != null && user.Email.ToLower() == normalizedEmail))
            .ToListAsync(cancellationToken);

        var existing = userMatches.SingleOrDefault(user => user.Id == LocalSuperadminId);
        if (existing is null && userMatches.Count > 0 ||
            existing is not null &&
            (!string.Equals(existing.Email, LocalSuperadminEmail, StringComparison.OrdinalIgnoreCase) ||
             userMatches.Any(user => user.Id != LocalSuperadminId)))
        {
            throw new InvalidOperationException(
                "Synthetic local Superadmin ID/email conflict; seed refused to overwrite another user.");
        }

        if (existing is not null && existing.OrganizationId != PlatformOrganization.Id)
        {
            throw new InvalidOperationException(
                "Synthetic local Superadmin is attached to a tenant Organization; seed refused to move it automatically.");
        }

        var previousIsSeeding = db.IsSeeding;
        db.IsSeeding = true;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var platformOrganization = platformMatches.SingleOrDefault();
            if (platformOrganization is null)
            {
                db.Organizations.Add(new OrganizationRow
                {
                    Id = PlatformOrganization.Id,
                    Name = PlatformOrganization.Name,
                    Cvr = PlatformOrganization.Cvr,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            else if (platformOrganization.Name != PlatformOrganization.Name)
            {
                var entry = db.Entry(platformOrganization);
                entry.Property(organization => organization.Name).CurrentValue = PlatformOrganization.Name;
                entry.Property(organization => organization.UpdatedAt).CurrentValue = now;
            }

            if (existing is null)
            {
                db.Users.Add(new UserDataRow
                {
                    Id = LocalSuperadminId,
                    OrganizationId = PlatformOrganization.Id,
                    FilialId = PlatformOrganization.Id,
                    DisplayName = LocalSuperadminDisplayName,
                    Email = LocalSuperadminEmail,
                    Phone = string.Empty,
                    Role = Roles.Superadmin,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
            else
            {
                existing.FilialId = PlatformOrganization.Id;
                existing.DisplayName = LocalSuperadminDisplayName;
                existing.Phone = string.Empty;
                existing.Role = Roles.Superadmin;
                existing.UpdatedAt = now;
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            db.IsSeeding = previousIsSeeding;
        }
    }
}
