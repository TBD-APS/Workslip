using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

public sealed class TenantIntegrityInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is SqlDbContext context)
        {
            ApplyAsync(context, CancellationToken.None).GetAwaiter().GetResult();
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is SqlDbContext context)
        {
            await ApplyAsync(context, cancellationToken);
        }

        return result;
    }

    private static async Task ApplyAsync(SqlDbContext context, CancellationToken cancellationToken)
    {
        context.ChangeTracker.DetectChanges();
        await EnsureFilialOwnershipAsync(context, cancellationToken);
        await EnsureInstallationSnapshotOwnershipAsync(context, cancellationToken);
    }

    private static async Task EnsureFilialOwnershipAsync(
        SqlDbContext context,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var filialSet = context.Set<OrganizationFilialRow>();
        var addedOrganizations = context.ChangeTracker
            .Entries<OrganizationRow>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToArray();

        foreach (var organization in addedOrganizations)
        {
            var hasTrackedDefault = context.ChangeTracker
                .Entries<OrganizationFilialRow>()
                .Any(entry =>
                    entry.State != EntityState.Deleted &&
                    entry.Entity.OrganizationId == organization.Id &&
                    entry.Entity.IsDefault);

            if (hasTrackedDefault)
            {
                continue;
            }

            filialSet.Add(new OrganizationFilialRow
            {
                // The default filial intentionally reuses the Organization ID.
                // It is deterministic for migration/retry purposes while future
                // additional filials can use ordinary independent GUIDs.
                Id = organization.Id,
                OrganizationId = organization.Id,
                Name = organization.Name,
                IsDefault = true,
                CreatedAt = organization.CreatedAt == default ? now : organization.CreatedAt,
                UpdatedAt = organization.UpdatedAt == default ? now : organization.UpdatedAt
            });
        }

        var addedUsers = context.ChangeTracker
            .Entries<UserDataRow>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToArray();
        var addedJobs = context.ChangeTracker
            .Entries<JobReportRow>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToArray();

        var organizationIds = addedUsers
            .Select(user => user.OrganizationId)
            .Concat(addedJobs.Select(job => job.OrganizationId))
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (organizationIds.Length == 0)
        {
            return;
        }

        var persistedFilials = await filialSet
            .AsNoTracking()
            .Where(filial => organizationIds.Contains(filial.OrganizationId))
            .Select(filial => new FilialKey(filial.OrganizationId, filial.Id, filial.IsDefault))
            .ToListAsync(cancellationToken);

        var trackedFilials = context.ChangeTracker
            .Entries<OrganizationFilialRow>()
            .Where(entry => entry.State == EntityState.Added && organizationIds.Contains(entry.Entity.OrganizationId))
            .Select(entry => new FilialKey(entry.Entity.OrganizationId, entry.Entity.Id, entry.Entity.IsDefault))
            .ToArray();

        var availableFilials = persistedFilials
            .Concat(trackedFilials)
            .Distinct()
            .ToArray();

        var defaultFilials = new Dictionary<Guid, Guid>();
        foreach (var organizationId in organizationIds)
        {
            var defaults = availableFilials
                .Where(filial => filial.OrganizationId == organizationId && filial.IsDefault)
                .Select(filial => filial.Id)
                .Distinct()
                .ToArray();

            if (defaults.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Organization '{organizationId}' must have exactly one default filial before users or jobs can be persisted.");
            }

            defaultFilials[organizationId] = defaults[0];
        }

        foreach (var user in addedUsers)
        {
            if (user.FilialId == Guid.Empty)
            {
                user.FilialId = defaultFilials[user.OrganizationId];
            }

            EnsureFilialBelongsToOrganization(user.OrganizationId, user.FilialId, availableFilials);
        }

        foreach (var job in addedJobs)
        {
            if (job.FilialId == Guid.Empty)
            {
                job.FilialId = defaultFilials[job.OrganizationId];
            }

            EnsureFilialBelongsToOrganization(job.OrganizationId, job.FilialId, availableFilials);
        }
    }

    private static void EnsureFilialBelongsToOrganization(
        Guid organizationId,
        Guid filialId,
        IReadOnlyCollection<FilialKey> availableFilials)
    {
        if (!availableFilials.Any(filial => filial.OrganizationId == organizationId && filial.Id == filialId))
        {
            throw new InvalidOperationException(
                $"Filial '{filialId}' does not belong to Organization '{organizationId}'.");
        }
    }

    private static async Task EnsureInstallationSnapshotOwnershipAsync(
        SqlDbContext context,
        CancellationToken cancellationToken)
    {
        var addedCategories = context.ChangeTracker
            .Entries<JobReportInstallationCategoryRow>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToArray();

        if (addedCategories.Length > 0)
        {
            var installationIds = addedCategories
                .Select(category => category.JobReportInstallationId)
                .Distinct()
                .ToArray();
            var installationOwners = context.ChangeTracker
                .Entries<JobReportInstallationRow>()
                .Where(entry => entry.State != EntityState.Deleted && installationIds.Contains(entry.Entity.Id))
                .Select(entry => new TenantOwner(entry.Entity.Id, entry.Entity.OrganizationId))
                .ToList();
            var trackedInstallationIds = installationOwners.Select(owner => owner.Id).ToHashSet();
            var missingInstallationIds = installationIds.Where(id => !trackedInstallationIds.Contains(id)).ToArray();

            if (missingInstallationIds.Length > 0)
            {
                installationOwners.AddRange(await context.JobReportInstallations
                    .AsNoTracking()
                    .Where(installation => missingInstallationIds.Contains(installation.Id))
                    .Select(installation => new TenantOwner(installation.Id, installation.OrganizationId))
                    .ToListAsync(cancellationToken));
            }

            ApplyTenantOwnership(
                addedCategories,
                installationOwners,
                category => category.JobReportInstallationId,
                category => category.OrganizationId,
                (category, organizationId) => category.OrganizationId = organizationId,
                "installation category snapshot");
        }

        var addedControlPoints = context.ChangeTracker
            .Entries<JobReportInstallationControlPointRow>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .ToArray();

        if (addedControlPoints.Length == 0)
        {
            return;
        }

        var categoryIds = addedControlPoints
            .Select(point => point.JobReportInstallationCategoryId)
            .Distinct()
            .ToArray();
        var categoryOwners = context.ChangeTracker
            .Entries<JobReportInstallationCategoryRow>()
            .Where(entry => entry.State != EntityState.Deleted && categoryIds.Contains(entry.Entity.Id))
            .Select(entry => new TenantOwner(entry.Entity.Id, entry.Entity.OrganizationId))
            .ToList();
        var trackedCategoryIds = categoryOwners.Select(owner => owner.Id).ToHashSet();
        var missingCategoryIds = categoryIds.Where(id => !trackedCategoryIds.Contains(id)).ToArray();

        if (missingCategoryIds.Length > 0)
        {
            categoryOwners.AddRange(await context.JobReportInstallationCategories
                .AsNoTracking()
                .Where(category => missingCategoryIds.Contains(category.Id))
                .Select(category => new TenantOwner(category.Id, category.OrganizationId))
                .ToListAsync(cancellationToken));
        }

        ApplyTenantOwnership(
            addedControlPoints,
            categoryOwners,
            point => point.JobReportInstallationCategoryId,
            point => point.OrganizationId,
            (point, organizationId) => point.OrganizationId = organizationId,
            "installation control-point snapshot");
    }

    private static void ApplyTenantOwnership<TEntity>(
        IReadOnlyCollection<TEntity> entities,
        IReadOnlyCollection<TenantOwner> owners,
        Func<TEntity, Guid> parentId,
        Func<TEntity, Guid> currentOrganizationId,
        Action<TEntity, Guid> setOrganizationId,
        string entityName)
    {
        var ownersById = owners
            .GroupBy(owner => owner.Id)
            .ToDictionary(group => group.Key, group => group.Select(owner => owner.OrganizationId).Distinct().ToArray());

        foreach (var entity in entities)
        {
            var id = parentId(entity);
            if (!ownersById.TryGetValue(id, out var organizationIds) || organizationIds.Length != 1)
            {
                throw new InvalidOperationException($"Could not resolve tenant ownership for {entityName} parent '{id}'.");
            }

            var ownerOrganizationId = organizationIds[0];
            var existingOrganizationId = currentOrganizationId(entity);
            if (existingOrganizationId == Guid.Empty)
            {
                setOrganizationId(entity, ownerOrganizationId);
                continue;
            }

            if (existingOrganizationId != ownerOrganizationId)
            {
                throw new InvalidOperationException($"{entityName} cannot cross Organization boundaries.");
            }
        }
    }

    private readonly record struct FilialKey(Guid OrganizationId, Guid Id, bool IsDefault);
    private readonly record struct TenantOwner(Guid Id, Guid OrganizationId);
}
