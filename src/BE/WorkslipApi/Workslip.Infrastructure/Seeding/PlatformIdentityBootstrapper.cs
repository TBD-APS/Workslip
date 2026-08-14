using System.Data;
using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

public sealed class PlatformIdentityBootstrapper(
    SqlDbContext db,
    ISuperadminEntraService entraService,
    IConfiguration configuration,
    ILogger<PlatformIdentityBootstrapper> logger)
{
    internal const string SuperadminEmailConfigurationKey = "WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL";

    internal static readonly Guid RotatableSuperadminId =
        new("F6F6F6F6-DA5B-4CC4-BBEB-07B40CAB806F");

    private static readonly Guid[] LegacySuperadminIds =
    [
        new("92779E5B-DA5B-4CC4-BBEB-07B40CAB806F"),
        new("D4D4D4D4-DA5B-4CC4-BBEB-07B40CAB806F"),
        new("E5E5E5E5-DA5B-4CC4-BBEB-07B40CAB806F")
    ];

    private static readonly Guid[] ReservedSuperadminIds =
        [RotatableSuperadminId, .. LegacySuperadminIds];

    private const string RotatableDisplayName = "Workslip Test Superadmin";

    public async Task BootstrapAsync(CancellationToken cancellationToken = default)
    {
        var configuredEmail = ResolveConfiguredEmail();
        db.ChangeTracker.Clear();

        IDbContextTransaction? transaction = null;
        CreateEntraUserResult? configuredEntraUser = null;
        var previousIsSeeding = db.IsSeeding;

        db.IsSeeding = true;
        try
        {
            if (db.Database.IsRelational())
            {
                transaction = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
            }

            var preflight = await PreflightAsync(configuredEmail, cancellationToken);
            StagePlatformOrganization(preflight.PlatformOrganization);
            await db.SaveChangesAsync(cancellationToken);

            configuredEntraUser = await entraService.EnsureSuperadminAsync(
                configuredEmail,
                RotatableDisplayName,
                cancellationToken);
            ValidateEntraResult(configuredEntraUser);
            await EnsureEntraIdentityIsNotOwnedByAnotherUserAsync(
                configuredEntraUser.EntraUserId,
                preflight.TargetUser?.Id,
                cancellationToken);

            var staleEntraIds = preflight.ReservedUsers
                .Where(user => preflight.TargetUser is null || user.Id != preflight.TargetUser.Id)
                .Select(user => user.EntraId)
                .Append(preflight.TargetUser?.EntraId ?? string.Empty)
                .Where(entraId =>
                    !string.IsNullOrWhiteSpace(entraId) &&
                    !string.Equals(
                        entraId,
                        configuredEntraUser.EntraUserId,
                        StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var staleEntraId in staleEntraIds)
            {
                await entraService.RevokeSuperadminAsync(staleEntraId, cancellationToken);
            }

            await StageConfiguredSuperadminAsync(
                preflight,
                configuredEmail,
                configuredEntraUser,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (Exception exception)
        {
            var failures = new List<Exception> { exception };

            if (transaction is not null)
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    db.ChangeTracker.Clear();
                }
                catch (Exception rollbackException)
                {
                    logger.LogError(
                        rollbackException,
                        "Rotatable platform Superadmin database rollback failed.");
                    failures.Add(rollbackException);
                }
            }

            if (configuredEntraUser?.Created == true &&
                !string.IsNullOrWhiteSpace(configuredEntraUser.EntraUserId))
            {
                try
                {
                    await entraService.DeleteUserAsync(
                        configuredEntraUser.EntraUserId,
                        CancellationToken.None);
                }
                catch (Exception compensationException)
                {
                    logger.LogError(
                        compensationException,
                        "Rotatable platform Superadmin Entra compensation failed.");
                    failures.Add(compensationException);
                }
            }

            if (failures.Count > 1)
            {
                throw new AggregateException(
                    "Rotatable platform Superadmin bootstrap failed and rollback was incomplete.",
                    failures);
            }

            ExceptionDispatchInfo.Capture(exception).Throw();
            throw;
        }
        finally
        {
            db.IsSeeding = previousIsSeeding;
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        logger.LogInformation(
            "Rotatable platform Superadmin reconciled. OrganizationId: {OrganizationId}. UserId: {UserId}. EntraIdentityCreated: {EntraIdentityCreated}.",
            PlatformOrganization.Id,
            RotatableSuperadminId,
            configuredEntraUser?.Created ?? false);
    }

    private string ResolveConfiguredEmail()
    {
        var email = configuration[SuperadminEmailConfigurationKey]?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new InvalidOperationException(
                $"Platform Superadmin bootstrap requires '{SuperadminEmailConfigurationKey}'. No fallback identity is allowed.");
        }

        var at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@') || at == email.Length - 1)
        {
            throw new InvalidOperationException(
                $"Platform Superadmin bootstrap configuration '{SuperadminEmailConfigurationKey}' is not a valid email address.");
        }

        return email;
    }

    private async Task<BootstrapPreflight> PreflightAsync(
        string configuredEmail,
        CancellationToken cancellationToken)
    {
        var platformOrganization = await ResolvePlatformOrganizationAsync(cancellationToken);

        var relevantUsers = await db.Users
            .AsNoTracking()
            .Where(user =>
                ReservedSuperadminIds.Contains(user.Id) ||
                user.OrganizationId == PlatformOrganization.Id ||
                (user.Email != null && user.Email.ToLower() == configuredEmail))
            .ToListAsync(cancellationToken);

        var emailOwners = relevantUsers
            .Where(user => NormalizeEmail(user.Email) == configuredEmail)
            .ToArray();
        if (emailOwners.Length > 1)
        {
            throw new InvalidOperationException(
                "Configured platform Superadmin email identifies more than one Workslip user.");
        }

        var emailOwner = emailOwners.SingleOrDefault();
        if (emailOwner is not null && !ReservedSuperadminIds.Contains(emailOwner.Id))
        {
            throw new InvalidOperationException(
                "Configured platform Superadmin email is already owned by a non-bootstrap Workslip user. Bootstrap refused to escalate or move that user.");
        }

        var unknownPlatformUser = relevantUsers.FirstOrDefault(user =>
            user.OrganizationId == PlatformOrganization.Id &&
            !ReservedSuperadminIds.Contains(user.Id));
        if (unknownPlatformUser is not null)
        {
            throw PlatformContamination("non-bootstrap users");
        }

        var rotatable = relevantUsers.SingleOrDefault(user => user.Id == RotatableSuperadminId);
        if (rotatable is not null && emailOwner is not null && emailOwner.Id != rotatable.Id)
        {
            throw new InvalidOperationException(
                "Configured platform Superadmin email conflicts with the existing rotatable platform identity.");
        }

        var target = rotatable ?? emailOwner;
        var reservedUsers = relevantUsers
            .Where(user => ReservedSuperadminIds.Contains(user.Id))
            .ToArray();

        foreach (var user in reservedUsers)
        {
            if (user.OrganizationId != PlatformOrganization.Id)
            {
                await EnsureNoTenantBoundReferencesAsync(user.Id, cancellationToken);
            }
        }

        await EnsurePlatformOrganizationHasNoOperationalDataAsync(cancellationToken);
        return new BootstrapPreflight(platformOrganization, target, reservedUsers);
    }

    private async Task<OrganizationRow?> ResolvePlatformOrganizationAsync(
        CancellationToken cancellationToken)
    {
        var reservedMatches = await db.Organizations
            .Where(organization =>
                organization.Id == PlatformOrganization.Id ||
                organization.Cvr == PlatformOrganization.Cvr)
            .ToListAsync(cancellationToken);

        if (reservedMatches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Reserved platform organization identity conflict: ID '{PlatformOrganization.Id}' and CVR '{PlatformOrganization.Cvr}' belong to different organizations.");
        }

        var platformOrganization = reservedMatches.SingleOrDefault();
        if (platformOrganization is not null &&
            (platformOrganization.Id != PlatformOrganization.Id ||
             platformOrganization.Cvr != PlatformOrganization.Cvr))
        {
            throw new InvalidOperationException(
                $"Reserved platform organization identity conflict: ID '{PlatformOrganization.Id}' and CVR '{PlatformOrganization.Cvr}' must identify the same organization.");
        }

        return platformOrganization;
    }

    private async Task EnsurePlatformOrganizationHasNoOperationalDataAsync(
        CancellationToken cancellationToken)
    {
        if (await db.Customers.AnyAsync(row => row.OrganizationId == PlatformOrganization.Id, cancellationToken))
            throw PlatformContamination("customers");
        if (await db.JobReports.AnyAsync(row => row.OrganizationId == PlatformOrganization.Id, cancellationToken))
            throw PlatformContamination("job reports");
        if (await db.JobAssignments.AnyAsync(row => row.OrganizationId == PlatformOrganization.Id, cancellationToken))
            throw PlatformContamination("job assignments");
        if (await db.JobReportLinks.AnyAsync(row => row.OrganizationId == PlatformOrganization.Id, cancellationToken))
            throw PlatformContamination("job report links");
        if (await db.JobEvents.AnyAsync(row => row.OrganizationId == PlatformOrganization.Id, cancellationToken))
            throw PlatformContamination("job events");
        if (await db.InviteTokens.AnyAsync(row => row.OrganizationId == PlatformOrganization.Id, cancellationToken))
            throw PlatformContamination("invite tokens");
        if (await db.Worksheets.AnyAsync(row => row.OrganizationId == PlatformOrganization.Id, cancellationToken))
            throw PlatformContamination("worksheets");
        if (await db.JobReportClosureFlags.AnyAsync(row => row.OrganizationId == PlatformOrganization.Id, cancellationToken))
            throw PlatformContamination("job closure selections");
        if (await db.JobReportInstallations.AnyAsync(row => row.OrganizationId == PlatformOrganization.Id, cancellationToken))
            throw PlatformContamination("job installations");
        if (await db.ControlCategoryRow.AnyAsync(row => row.OrganizationId == PlatformOrganization.Id, cancellationToken))
            throw PlatformContamination("control categories");
        if (await db.ControlPointRow.AnyAsync(row => row.OrganizationId == PlatformOrganization.Id, cancellationToken))
            throw PlatformContamination("control points");
        if (await db.InstallationTypeDefinitions.AnyAsync(row => row.OrganizationId == PlatformOrganization.Id, cancellationToken))
            throw PlatformContamination("installation definitions");
    }

    private async Task EnsureNoTenantBoundReferencesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (await db.JobAssignments.AnyAsync(
                row => row.UserId == userId || row.AssignedByUserId == userId,
                cancellationToken))
            throw TenantReferenceConflict(userId, "job assignments");
        if (await db.JobEvents.AnyAsync(row => row.ActorId == userId, cancellationToken))
            throw TenantReferenceConflict(userId, "job events");
        if (await db.Worksheets.AnyAsync(row => row.UserId == userId, cancellationToken))
            throw TenantReferenceConflict(userId, "worksheets");
    }

    private async Task EnsureEntraIdentityIsNotOwnedByAnotherUserAsync(
        string entraUserId,
        Guid? targetUserId,
        CancellationToken cancellationToken)
    {
        var conflictingOwner = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                user =>
                    user.EntraId == entraUserId &&
                    (!targetUserId.HasValue || user.Id != targetUserId.Value) &&
                    !LegacySuperadminIds.Contains(user.Id),
                cancellationToken);

        if (conflictingOwner is not null)
        {
            throw new InvalidOperationException(
                $"Configured platform Superadmin Entra identity is already linked to Workslip user '{conflictingOwner.Id}'.");
        }
    }

    private async Task StageConfiguredSuperadminAsync(
        BootstrapPreflight preflight,
        string configuredEmail,
        CreateEntraUserResult entraUser,
        CancellationToken cancellationToken)
    {
        var trackedReservedUsers = await db.Users
            .Where(user => ReservedSuperadminIds.Contains(user.Id))
            .ToListAsync(cancellationToken);

        var targetId = preflight.TargetUser?.Id ?? RotatableSuperadminId;
        foreach (var staleUser in trackedReservedUsers.Where(user => user.Id != targetId).ToArray())
        {
            await DeleteEphemeralReferencesAsync(staleUser.Id, cancellationToken);
            db.Users.Remove(staleUser);
        }

        var target = trackedReservedUsers.SingleOrDefault(user => user.Id == targetId);
        var now = DateTimeOffset.UtcNow;
        if (target is null)
        {
            db.Users.Add(new UserDataRow
            {
                Id = RotatableSuperadminId,
                OrganizationId = PlatformOrganization.Id,
                FilialId = PlatformOrganization.Id,
                DisplayName = RotatableDisplayName,
                Email = configuredEmail,
                Phone = string.Empty,
                Role = Roles.Superadmin,
                EntraId = entraUser.EntraUserId,
                EntraEmail = entraUser.EntraMail,
                CreatedAt = now,
                UpdatedAt = now
            });
            return;
        }

        if (target.OrganizationId != PlatformOrganization.Id)
        {
            await DeleteEphemeralReferencesAsync(target.Id, cancellationToken);
        }

        target.OrganizationId = PlatformOrganization.Id;
        target.FilialId = PlatformOrganization.Id;
        target.DisplayName = RotatableDisplayName;
        target.Email = configuredEmail;
        target.Phone = string.Empty;
        target.Role = Roles.Superadmin;
        target.EntraId = entraUser.EntraUserId;
        target.EntraEmail = entraUser.EntraMail;
        target.UpdatedAt = now;
    }

    private async Task DeleteEphemeralReferencesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            await db.JobViews.Where(view => view.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await db.PushSubscriptions.Where(subscription => subscription.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            await db.NotificationQueue.Where(notification => notification.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            return;
        }

        db.JobViews.RemoveRange(db.JobViews.Where(view => view.UserId == userId));
        db.PushSubscriptions.RemoveRange(db.PushSubscriptions.Where(subscription => subscription.UserId == userId));
        db.NotificationQueue.RemoveRange(db.NotificationQueue.Where(notification => notification.UserId == userId));
    }

    private void StagePlatformOrganization(OrganizationRow? platformOrganization)
    {
        var now = DateTimeOffset.UtcNow;
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
            return;
        }

        if (platformOrganization.Name != PlatformOrganization.Name)
        {
            platformOrganization.Name = PlatformOrganization.Name;
            platformOrganization.UpdatedAt = now;
        }
    }

    private static void ValidateEntraResult(CreateEntraUserResult entraUser)
    {
        if (string.IsNullOrWhiteSpace(entraUser.EntraUserId))
            throw new InvalidOperationException("Graph returned no Entra user ID for the configured platform Superadmin.");
        if (string.IsNullOrWhiteSpace(entraUser.EntraMail))
            throw new InvalidOperationException("Graph returned no Entra mail identity for the configured platform Superadmin.");
    }

    private static string NormalizeEmail(string? email) =>
        email?.Trim().ToLowerInvariant() ?? string.Empty;

    private static InvalidOperationException PlatformContamination(string relation) =>
        new($"Reserved platform organization '{PlatformOrganization.Id}' contains {relation}. Bootstrap refused to mix platform identity with tenant data.");

    private static InvalidOperationException TenantReferenceConflict(Guid userId, string relation) =>
        new($"Legacy platform Superadmin '{userId}' has tenant-bound references in {relation}; rotation was refused.");

    private sealed record BootstrapPreflight(
        OrganizationRow? PlatformOrganization,
        UserDataRow? TargetUser,
        IReadOnlyList<UserDataRow> ReservedUsers);
}
