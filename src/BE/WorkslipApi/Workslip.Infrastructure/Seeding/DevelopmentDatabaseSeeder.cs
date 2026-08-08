using Microsoft.Extensions.Logging;
using Workslip.Application.Users;

namespace Workslip.Infrastructure.Schema;

public sealed class DevelopmentDatabaseSeeder(
    SqlDbContext db,
    InstallationBaselineProvisioner installationBaselineProvisioner,
    ISuperadminEntraService entraService,
    ILogger<DevelopmentDatabaseSeeder> logger)
{
    private static readonly CanonicalSuperadminDefinition[] CanonicalSuperadmins =
    [
        new(
            new Guid("92779E5B-DA5B-4CC4-BBEB-07B40CAB806F"),
            "Rasmus Bak Jakobsen",
            "rasmusvm6@hotmail.com",
            "28929173"),
        new(
            new Guid("D4D4D4D4-DA5B-4CC4-BBEB-07B40CAB806F"),
            "Mahad",
            "mahad8@outlook.dk",
            string.Empty),
        new(
            new Guid("E5E5E5E5-DA5B-4CC4-BBEB-07B40CAB806F"),
            "Mathias Lambæk",
            "mathiaslt1@hotmail.dk",
            string.Empty)
    ];

    private static readonly Guid[] CanonicalSuperadminIds =
        CanonicalSuperadmins.Select(definition => definition.Id).ToArray();

    private static readonly string[] CanonicalSuperadminEmails =
        CanonicalSuperadmins.Select(definition => definition.Email).ToArray();

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        IDbContextTransaction? transaction = null;
        var createdEntraUserIds = new List<string>();
        var createdEntraUserIdSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var previousIsSeeding = db.IsSeeding;
        IReadOnlyList<ResolvedSuperadmin>? resolvedSuperadmins = null;
        IReadOnlyDictionary<Guid, CreateEntraUserResult>? entraUsers = null;

        db.IsSeeding = true;
        try
        {
            if (db.Database.IsRelational())
            {
                transaction = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
            }

            var platformOrganization = await PreflightPlatformOrganizationAsync(cancellationToken);
            resolvedSuperadmins = await PreflightCanonicalSuperadminsAsync(cancellationToken);

            StagePlatformOrganization(platformOrganization);
            await db.SaveChangesAsync(cancellationToken);

            await DatabaseSeeder.Seed(db, installationBaselineProvisioner, cancellationToken);
            db.IsSeeding = true;

            var resolvedEntraUsers = new Dictionary<Guid, CreateEntraUserResult>();
            foreach (var resolvedSuperadmin in resolvedSuperadmins)
            {
                var definition = resolvedSuperadmin.Definition;
                var entraUser = await entraService.EnsureSuperadminAsync(
                    definition.Email,
                    definition.DisplayName,
                    cancellationToken);

                // Record every usable newly-created identity before any later
                // validation can fail so compensation cannot lose it.
                if (entraUser.Created &&
                    !string.IsNullOrWhiteSpace(entraUser.EntraUserId) &&
                    createdEntraUserIdSet.Add(entraUser.EntraUserId))
                {
                    createdEntraUserIds.Add(entraUser.EntraUserId);
                }

                ValidateEntraIdentity(definition, entraUser, resolvedEntraUsers.Values);
                resolvedEntraUsers.Add(definition.Id, entraUser);
            }

            entraUsers = resolvedEntraUsers;
            await ValidateEntraIdentityOwnershipAsync(resolvedSuperadmins, entraUsers, cancellationToken);

            foreach (var resolvedSuperadmin in resolvedSuperadmins)
            {
                await StageSuperadminReconciliationAsync(
                    resolvedSuperadmin,
                    entraUsers[resolvedSuperadmin.Definition.Id],
                    cancellationToken);
            }

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
                        "Development seed database rollback failed.");
                    failures.Add(rollbackException);
                }
            }

            for (var index = createdEntraUserIds.Count - 1; index >= 0; index--)
            {
                var entraUserId = createdEntraUserIds[index];
                try
                {
                    await entraService.DeleteUserAsync(entraUserId, CancellationToken.None);
                }
                catch (Exception compensationException)
                {
                    logger.LogError(
                        compensationException,
                        "Development Superadmin Entra compensation failed. EntraUserId: {EntraUserId}.",
                        entraUserId);
                    failures.Add(compensationException);
                }
            }

            if (failures.Count > 1)
            {
                throw new AggregateException(
                    "Development seed failed and one or more rollback operations also failed.",
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

        if (resolvedSuperadmins is null || entraUsers is null)
        {
            return;
        }

        foreach (var resolvedSuperadmin in resolvedSuperadmins)
        {
            var definition = resolvedSuperadmin.Definition;
            var entraUser = entraUsers[definition.Id];
            logger.LogInformation(
                "Development platform Superadmin reconciled. UserId: {UserId}. OrganizationId: {OrganizationId}. EntraUserId: {EntraUserId}. EntraIdentityCreated: {EntraIdentityCreated}.",
                definition.Id,
                PlatformOrganization.Id,
                entraUser.EntraUserId,
                entraUser.Created);
        }
    }

    private async Task<OrganizationRow?> PreflightPlatformOrganizationAsync(
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

        await EnsurePlatformOrganizationIsUncontaminatedAsync(cancellationToken);
        return platformOrganization;
    }

    private async Task EnsurePlatformOrganizationIsUncontaminatedAsync(
        CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(
                user =>
                    user.OrganizationId == PlatformOrganization.Id &&
                    !CanonicalSuperadminIds.Contains(user.Id),
                cancellationToken))
        {
            throw PlatformContamination("non-canonical users");
        }

        if (await db.Customers.AnyAsync(
                row => row.OrganizationId == PlatformOrganization.Id,
                cancellationToken))
        {
            throw PlatformContamination("customers");
        }

        if (await db.JobReports.AnyAsync(
                row => row.OrganizationId == PlatformOrganization.Id,
                cancellationToken))
        {
            throw PlatformContamination("job reports");
        }

        if (await db.JobAssignments.AnyAsync(
                row => row.OrganizationId == PlatformOrganization.Id,
                cancellationToken))
        {
            throw PlatformContamination("job assignments");
        }

        if (await db.JobReportLinks.AnyAsync(
                row => row.OrganizationId == PlatformOrganization.Id,
                cancellationToken))
        {
            throw PlatformContamination("job report links");
        }

        if (await db.JobEvents.AnyAsync(
                row => row.OrganizationId == PlatformOrganization.Id,
                cancellationToken))
        {
            throw PlatformContamination("job events");
        }

        if (await db.InviteTokens.AnyAsync(
                row => row.OrganizationId == PlatformOrganization.Id,
                cancellationToken))
        {
            throw PlatformContamination("invite tokens");
        }

        if (await db.Worksheets.AnyAsync(
                row => row.OrganizationId == PlatformOrganization.Id,
                cancellationToken))
        {
            throw PlatformContamination("worksheets");
        }

        if (await db.JobReportClosureFlags.AnyAsync(
                row => row.OrganizationId == PlatformOrganization.Id,
                cancellationToken))
        {
            throw PlatformContamination("job closure selections");
        }

        if (await db.JobReportInstallations.AnyAsync(
                row => row.OrganizationId == PlatformOrganization.Id,
                cancellationToken))
        {
            throw PlatformContamination("job installations");
        }

        if (await db.ControlCategoryRow.AnyAsync(
                row => row.OrganizationId == PlatformOrganization.Id,
                cancellationToken))
        {
            throw PlatformContamination("control categories");
        }

        if (await db.ControlPointRow.AnyAsync(
                row => row.OrganizationId == PlatformOrganization.Id,
                cancellationToken))
        {
            throw PlatformContamination("control points");
        }

        if (await db.InstallationTypeDefinitions.AnyAsync(
                row => row.OrganizationId == PlatformOrganization.Id,
                cancellationToken))
        {
            throw PlatformContamination("installation definitions");
        }
    }

    private async Task<IReadOnlyList<ResolvedSuperadmin>> PreflightCanonicalSuperadminsAsync(
        CancellationToken cancellationToken)
    {
        var matchingUsers = await db.Users
            .AsNoTracking()
            .Where(user =>
                CanonicalSuperadminIds.Contains(user.Id) ||
                (user.Email != null &&
                 CanonicalSuperadminEmails.Contains(user.Email.Trim().ToLower())))
            .ToListAsync(cancellationToken);

        var resolvedSuperadmins = new List<ResolvedSuperadmin>(CanonicalSuperadmins.Length);
        foreach (var definition in CanonicalSuperadmins)
        {
            var userWithCanonicalId = matchingUsers.SingleOrDefault(user => user.Id == definition.Id);
            var usersWithCanonicalEmail = matchingUsers
                .Where(user => NormalizeEmail(user.Email) == definition.Email)
                .ToArray();

            if (userWithCanonicalId is null)
            {
                if (usersWithCanonicalEmail.Length > 0)
                {
                    throw CanonicalIdentityConflict(definition);
                }

                resolvedSuperadmins.Add(new ResolvedSuperadmin(definition, ExistingUser: null));
                continue;
            }

            if (NormalizeEmail(userWithCanonicalId.Email) != definition.Email ||
                usersWithCanonicalEmail.Any(user => user.Id != definition.Id))
            {
                throw CanonicalIdentityConflict(definition);
            }

            await EnsureNoTenantBoundReferencesAsync(definition.Id, cancellationToken);
            resolvedSuperadmins.Add(new ResolvedSuperadmin(
                definition,
                UserSnapshot.From(userWithCanonicalId)));
        }

        return resolvedSuperadmins;
    }

    private async Task EnsureNoTenantBoundReferencesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (await db.JobAssignments.AnyAsync(
                row => row.UserId == userId || row.AssignedByUserId == userId,
                cancellationToken))
        {
            throw TenantReferenceConflict(userId, "job assignments");
        }

        if (await db.JobEvents.AnyAsync(
                row => row.ActorId == userId,
                cancellationToken))
        {
            throw TenantReferenceConflict(userId, "job events");
        }

        if (await db.Worksheets.AnyAsync(
                row => row.UserId == userId,
                cancellationToken))
        {
            throw TenantReferenceConflict(userId, "worksheets");
        }
    }

    private async Task DeleteEphemeralReferencesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (db.Database.IsRelational())
        {
            await db.JobViews
                .Where(view => view.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);
            await db.PushSubscriptions
                .Where(subscription => subscription.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);
            await db.NotificationQueue
                .Where(notification => notification.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);
            return;
        }

        db.JobViews.RemoveRange(db.JobViews.Where(view => view.UserId == userId));
        db.PushSubscriptions.RemoveRange(db.PushSubscriptions.Where(subscription => subscription.UserId == userId));
        db.NotificationQueue.RemoveRange(db.NotificationQueue.Where(notification => notification.UserId == userId));
    }

    private void StagePlatformOrganization(OrganizationRow? platformOrganization)
    {
        var timestamp = DateTimeOffset.UtcNow;
        if (platformOrganization is null)
        {
            db.Organizations.Add(new OrganizationRow
            {
                Id = PlatformOrganization.Id,
                Name = PlatformOrganization.Name,
                Cvr = PlatformOrganization.Cvr,
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            });
            return;
        }

        if (platformOrganization.Name == PlatformOrganization.Name)
        {
            return;
        }

        var entry = db.Entry(platformOrganization);
        entry.Property(organization => organization.Name).CurrentValue = PlatformOrganization.Name;
        entry.Property(organization => organization.UpdatedAt).CurrentValue = timestamp;
    }

    private async Task ValidateEntraIdentityOwnershipAsync(
        IReadOnlyList<ResolvedSuperadmin> resolvedSuperadmins,
        IReadOnlyDictionary<Guid, CreateEntraUserResult> entraUsers,
        CancellationToken cancellationToken)
    {
        var canonicalIds = resolvedSuperadmins
            .Select(resolved => resolved.Definition.Id)
            .ToArray();
        var entraUserIds = entraUsers.Values
            .Select(user => user.EntraUserId)
            .ToArray();

        var conflictingOwner = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                user =>
                    !canonicalIds.Contains(user.Id) &&
                    entraUserIds.Contains(user.EntraId),
                cancellationToken);

        if (conflictingOwner is not null)
        {
            throw new InvalidOperationException(
                $"Development Superadmin Entra identity '{conflictingOwner.EntraId}' is already linked to non-canonical Workslip user '{conflictingOwner.Id}'.");
        }
    }

    private async Task StageSuperadminReconciliationAsync(
        ResolvedSuperadmin resolvedSuperadmin,
        CreateEntraUserResult entraUser,
        CancellationToken cancellationToken)
    {
        var definition = resolvedSuperadmin.Definition;
        var timestamp = DateTimeOffset.UtcNow;
        if (resolvedSuperadmin.ExistingUser is null)
        {
            db.Users.Add(new UserDataRow
            {
                Id = definition.Id,
                OrganizationId = PlatformOrganization.Id,
                DisplayName = definition.DisplayName,
                Email = definition.Email,
                Phone = definition.Phone,
                Role = Roles.Superadmin,
                EntraId = entraUser.EntraUserId,
                EntraEmail = entraUser.EntraMail,
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            });
            return;
        }

        var existing = resolvedSuperadmin.ExistingUser;
        if (existing.OrganizationId != PlatformOrganization.Id)
        {
            await DeleteEphemeralReferencesAsync(definition.Id, cancellationToken);
        }

        var requiresUpdate = !existing.MatchesDesired(definition, entraUser);
        var reconciledUpdatedAt = requiresUpdate ? timestamp : existing.UpdatedAt;
        if (db.Database.IsRelational())
        {
            var affectedRows = await db.Users
                .Where(user =>
                    user.Id == existing.Id &&
                    user.OrganizationId == existing.OrganizationId &&
                    user.DisplayName == existing.DisplayName &&
                    user.Email == existing.Email &&
                    user.Phone == existing.Phone &&
                    user.Role == existing.Role &&
                    user.EntraId == existing.EntraId &&
                    user.EntraEmail == existing.EntraEmail &&
                    user.UpdatedAt == existing.UpdatedAt)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(user => user.OrganizationId, PlatformOrganization.Id)
                        .SetProperty(user => user.DisplayName, definition.DisplayName)
                        .SetProperty(user => user.Email, definition.Email)
                        .SetProperty(user => user.Phone, definition.Phone)
                        .SetProperty(user => user.Role, Roles.Superadmin)
                        .SetProperty(user => user.EntraId, entraUser.EntraUserId)
                        .SetProperty(user => user.EntraEmail, entraUser.EntraMail)
                        .SetProperty(user => user.UpdatedAt, reconciledUpdatedAt),
                    cancellationToken);

            if (affectedRows != 1)
            {
                throw ConcurrentCanonicalUserChange(definition.Id);
            }

            return;
        }

        var trackedUser = await db.Users.SingleOrDefaultAsync(
            user => user.Id == definition.Id,
            cancellationToken);
        if (trackedUser is null || !existing.Matches(trackedUser))
        {
            throw ConcurrentCanonicalUserChange(definition.Id);
        }

        if (!requiresUpdate)
        {
            return;
        }

        trackedUser.OrganizationId = PlatformOrganization.Id;
        trackedUser.DisplayName = definition.DisplayName;
        trackedUser.Email = definition.Email;
        trackedUser.Phone = definition.Phone;
        trackedUser.Role = Roles.Superadmin;
        trackedUser.EntraId = entraUser.EntraUserId;
        trackedUser.EntraEmail = entraUser.EntraMail;
        trackedUser.UpdatedAt = reconciledUpdatedAt;
    }

    private static void ValidateEntraIdentity(
        CanonicalSuperadminDefinition definition,
        CreateEntraUserResult entraUser,
        IEnumerable<CreateEntraUserResult> alreadyResolvedUsers)
    {
        if (string.IsNullOrWhiteSpace(entraUser.EntraUserId))
        {
            throw new InvalidOperationException(
                $"Graph returned no Entra user ID for canonical development Superadmin '{definition.Id}'.");
        }

        if (alreadyResolvedUsers.Any(
                resolved => string.Equals(
                    resolved.EntraUserId,
                    entraUser.EntraUserId,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Graph returned Entra user ID '{entraUser.EntraUserId}' for more than one canonical development Superadmin.");
        }
    }

    private static string NormalizeEmail(string email) =>
        email.Trim().ToLowerInvariant();

    private static InvalidOperationException PlatformContamination(string relation) =>
        new(
            $"Reserved platform organization '{PlatformOrganization.Id}' contains {relation}. Remove the customer or operational data before development startup.");

    private static InvalidOperationException CanonicalIdentityConflict(
        CanonicalSuperadminDefinition definition) =>
        new(
            $"Development Superadmin identity conflict: canonical ID '{definition.Id}' and normalized email '{definition.Email}' do not identify the same Workslip user.");

    private static InvalidOperationException TenantReferenceConflict(Guid userId, string relation) =>
        new(
            $"Canonical development Superadmin '{userId}' has tenant-bound references in {relation} and cannot be moved to '{PlatformOrganization.Name}'.");

    private static InvalidOperationException ConcurrentCanonicalUserChange(Guid userId) =>
        new(
            $"Canonical development Superadmin '{userId}' changed after seed preflight; the platform reconciliation was aborted.");

    private sealed record CanonicalSuperadminDefinition(
        Guid Id,
        string DisplayName,
        string Email,
        string Phone);

    private sealed record ResolvedSuperadmin(
        CanonicalSuperadminDefinition Definition,
        UserSnapshot? ExistingUser);

    private sealed record UserSnapshot(
        Guid Id,
        Guid OrganizationId,
        string DisplayName,
        string Email,
        string Phone,
        string Role,
        string EntraId,
        string EntraEmail,
        DateTimeOffset UpdatedAt)
    {
        internal static UserSnapshot From(UserDataRow user) =>
            new(
                user.Id,
                user.OrganizationId,
                user.DisplayName,
                user.Email,
                user.Phone,
                user.Role,
                user.EntraId,
                user.EntraEmail,
                user.UpdatedAt);

        internal bool Matches(UserDataRow user) =>
            user.Id == Id &&
            user.OrganizationId == OrganizationId &&
            user.DisplayName == DisplayName &&
            user.Email == Email &&
            user.Phone == Phone &&
            user.Role == Role &&
            user.EntraId == EntraId &&
            user.EntraEmail == EntraEmail &&
            user.UpdatedAt == UpdatedAt;

        internal bool MatchesDesired(
            CanonicalSuperadminDefinition definition,
            CreateEntraUserResult entraUser) =>
            OrganizationId == PlatformOrganization.Id &&
            DisplayName == definition.DisplayName &&
            Email == definition.Email &&
            Phone == definition.Phone &&
            Role == Roles.Superadmin &&
            EntraId == entraUser.EntraUserId &&
            EntraEmail == entraUser.EntraMail;
    }
}
