using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Workslip.Domain.Models;
using static Workslip.Infrastructure.Schema.AuditEntryValueReader;

namespace Workslip.Infrastructure.Schema;

internal interface IAuditEntityPolicy
{
    bool CanHandle(EntityEntry entry);
    IReadOnlyList<AuditEntry> BuildEvents(AuditBuildContext context, EntityEntry entry, AuditChangeCollector collector);
}

internal sealed class DefaultAuditPolicy : IAuditEntityPolicy
{
    public bool CanHandle(EntityEntry entry) => true;

    public IReadOnlyList<AuditEntry> BuildEvents(AuditBuildContext context, EntityEntry entry, AuditChangeCollector collector)
    {
        var auditEntry = collector.BuildBaseEntry(context, entry);
        if (entry.State is EntityState.Deleted)
            collector.CollectDeletedForeignKeyDisplays(context, entry, auditEntry);

        AuditChangeCollector.Finalize(auditEntry);
        return AuditChangeCollector.ShouldSkip(auditEntry) ? [] : [auditEntry];
    }
}


internal sealed class JobReportAuditPolicy : IAuditEntityPolicy
{
    public bool CanHandle(EntityEntry entry) => entry.Entity is JobReportRow;

    public IReadOnlyList<AuditEntry> BuildEvents(AuditBuildContext context, EntityEntry entry, AuditChangeCollector collector)
    {
        var auditEntry = collector.BuildBaseEntry(context, entry);
        ReplaceWorkKindIdWithDisplayValue(context, auditEntry);
        ReplaceCustomerIdWithDisplayValue(context, entry, auditEntry);

        AuditChangeCollector.Finalize(auditEntry);
        return AuditChangeCollector.ShouldSkip(auditEntry) ? [] : [auditEntry];
    }

    private static void ReplaceWorkKindIdWithDisplayValue(AuditBuildContext context, AuditEntry auditEntry)
    {
        var hasBefore = auditEntry.BeforeValues.TryGetValue(nameof(JobReportRow.WorkKindId), out var beforeValue);
        var hasAfter = auditEntry.AfterValues.TryGetValue(nameof(JobReportRow.WorkKindId), out var afterValue);
        if (!hasBefore && !hasAfter)
            return;

        auditEntry.BeforeValues.Remove(nameof(JobReportRow.WorkKindId));
        auditEntry.AfterValues.Remove(nameof(JobReportRow.WorkKindId));

        if (hasBefore)
            auditEntry.BeforeValues[AuditFields.WorkKind] = context.DisplayResolver.ResolveWorkKindDisplayValue(context, beforeValue as Guid?);

        if (hasAfter)
            auditEntry.AfterValues[AuditFields.WorkKind] = context.DisplayResolver.ResolveWorkKindDisplayValue(context, afterValue as Guid?);
    }

    private static void ReplaceCustomerIdWithDisplayValue(AuditBuildContext context, EntityEntry entry, AuditEntry auditEntry)
    {
        var hasBefore = auditEntry.BeforeValues.TryGetValue(nameof(JobReportRow.CustomerId), out var beforeValue);
        var hasAfter = auditEntry.AfterValues.TryGetValue(nameof(JobReportRow.CustomerId), out var afterValue);
        if (!hasBefore && !hasAfter)
            return;

        var organizationId = GetGuid(entry, nameof(JobReportRow.OrganizationId), useOriginalValue: entry.State == EntityState.Deleted)
            ?? auditEntry.OrganizationId;

        auditEntry.BeforeValues.Remove(nameof(JobReportRow.CustomerId));
        auditEntry.AfterValues.Remove(nameof(JobReportRow.CustomerId));

        if (hasBefore)
            auditEntry.BeforeValues[AuditFields.Customer] = context.DisplayResolver.ResolveCustomerDisplayValue(context, organizationId, beforeValue as Guid?);

        if (hasAfter)
            auditEntry.AfterValues[AuditFields.Customer] = context.DisplayResolver.ResolveCustomerDisplayValue(context, organizationId, afterValue as Guid?);
    }
}

internal sealed class JobAssignmentAuditPolicy : IAuditEntityPolicy
{
    public bool CanHandle(EntityEntry entry) => entry.Entity is JobAssignmentRow;

    public IReadOnlyList<AuditEntry> BuildEvents(AuditBuildContext context, EntityEntry entry, AuditChangeCollector collector)
    {
        if (entry.State is not (EntityState.Added or EntityState.Deleted or EntityState.Modified))
            return [];

        var reportId = GetGuid(entry, nameof(JobAssignmentRow.ReportId), useOriginalValue: entry.State == EntityState.Deleted);
        if (reportId is null || !context.ProcessedAssignmentReportIds.Add(reportId.Value))
            return [];

        var assignmentEntries = context.DbContext.ChangeTracker.Entries<JobAssignmentRow>()
            .Where(e => e.State is EntityState.Added or EntityState.Deleted or EntityState.Modified)
            .Where(e => GetGuid(e, nameof(JobAssignmentRow.ReportId), useOriginalValue: e.State == EntityState.Deleted) == reportId.Value)
            .ToList();

        if (assignmentEntries.Count == 0)
            return [];

        var auditEntry = collector.BuildBaseEntry(context, entry);
        auditEntry.BeforeValues.Clear();
        auditEntry.AfterValues.Clear();
        auditEntry.EventType = ResolveEventType(assignmentEntries);

        var useDedicatedKeys = assignmentEntries.Count > 1;
        foreach (var assignmentEntry in assignmentEntries)
        {
            var organizationId = GetGuid(assignmentEntry, nameof(JobAssignmentRow.OrganizationId), useOriginalValue: assignmentEntry.State == EntityState.Deleted) ?? auditEntry.OrganizationId;
            var beforeUserId = GetGuid(assignmentEntry, nameof(JobAssignmentRow.UserId), useOriginalValue: true);
            var afterUserId = GetGuid(assignmentEntry, nameof(JobAssignmentRow.UserId));
            var beforeUser = beforeUserId is null
                ? null
                : context.DisplayResolver.ResolveUserDisplayValue(context, organizationId, beforeUserId.Value);
            var afterUser = afterUserId is null
                ? null
                : context.DisplayResolver.ResolveUserDisplayValue(context, organizationId, afterUserId.Value);

            var displayUser = assignmentEntry.State == EntityState.Deleted ? beforeUser : afterUser;
            var key = useDedicatedKeys ? $"{AuditDisplayNames.Labels[AuditFields.AssignedUser]} / {displayUser}" : AuditFields.AssignedUser;

            switch (assignmentEntry.State)
            {
                case EntityState.Added:
                    auditEntry.AfterValues[key] = afterUser;
                    break;

                case EntityState.Deleted:
                    auditEntry.BeforeValues[key] = beforeUser;
                    break;

                case EntityState.Modified when beforeUser != afterUser:
                    auditEntry.BeforeValues[key] = beforeUser;
                    auditEntry.AfterValues[key] = afterUser;
                    break;
            }
        }

        auditEntry.Summary = auditEntry.EventType switch
        {
            AuditEventTypes.Added when !useDedicatedKeys => string.Format(AuditSummaryTemplates.AssignmentAdded, auditEntry.AfterValues[AuditFields.AssignedUser]),
            AuditEventTypes.Deleted when !useDedicatedKeys => string.Format(AuditSummaryTemplates.AssignmentDeleted, auditEntry.BeforeValues[AuditFields.AssignedUser]),
            AuditEventTypes.Modified when !useDedicatedKeys => string.Format(AuditSummaryTemplates.AssignmentChanged, auditEntry.BeforeValues[AuditFields.AssignedUser], auditEntry.AfterValues[AuditFields.AssignedUser]),
            AuditEventTypes.Added => AuditSummaryTemplates.AssignmentsAdded,
            AuditEventTypes.Deleted => AuditSummaryTemplates.AssignmentsDeleted,
            _ => AuditSummaryTemplates.AssignmentsChanged
        };

        AuditChangeCollector.Finalize(auditEntry);
        return AuditChangeCollector.ShouldSkip(auditEntry) ? [] : [auditEntry];
    }

    private static string ResolveEventType(IReadOnlyCollection<EntityEntry<JobAssignmentRow>> entries)
    {
        if (entries.All(e => e.State == EntityState.Added))
            return AuditEventTypes.Added;

        if (entries.All(e => e.State == EntityState.Deleted))
            return AuditEventTypes.Deleted;

        return AuditEventTypes.Modified;
    }
}

internal sealed class WorksheetAuditPolicy : IAuditEntityPolicy
{
    public bool CanHandle(EntityEntry entry) => entry.Entity is WorksheetRow;

    public IReadOnlyList<AuditEntry> BuildEvents(AuditBuildContext context, EntityEntry entry, AuditChangeCollector collector)
    {
        var auditEntry = collector.BuildBaseEntry(context, entry);
        ReplaceJobIdWithDisplayValue(context, entry, auditEntry);
        ReplaceUserIdWithDisplayValue(context, entry, auditEntry);
        SetWorksheetSummary(auditEntry);

        AuditChangeCollector.Finalize(auditEntry);
        return AuditChangeCollector.ShouldSkip(auditEntry) ? [] : [auditEntry];
    }

    private static void ReplaceJobIdWithDisplayValue(AuditBuildContext context, EntityEntry entry, AuditEntry auditEntry)
    {
        var hasBefore = auditEntry.BeforeValues.TryGetValue(nameof(WorksheetRow.JobId), out var beforeValue);
        var hasAfter = auditEntry.AfterValues.TryGetValue(nameof(WorksheetRow.JobId), out var afterValue);
        if (!hasBefore && !hasAfter)
            return;

        var organizationId = GetGuid(entry, nameof(WorksheetRow.OrganizationId), useOriginalValue: entry.State == EntityState.Deleted)
            ?? auditEntry.OrganizationId;

        auditEntry.BeforeValues.Remove(nameof(WorksheetRow.JobId));
        auditEntry.AfterValues.Remove(nameof(WorksheetRow.JobId));

        if (hasBefore && beforeValue is Guid beforeReportId)
            auditEntry.BeforeValues[AuditFields.Report] = context.DisplayResolver.ResolveReportDisplayValue(context, organizationId, beforeReportId);

        if (hasAfter && afterValue is Guid afterReportId)
            auditEntry.AfterValues[AuditFields.Report] = context.DisplayResolver.ResolveReportDisplayValue(context, organizationId, afterReportId);
    }

    private static void ReplaceUserIdWithDisplayValue(AuditBuildContext context, EntityEntry entry, AuditEntry auditEntry)
    {
        var hasBefore = auditEntry.BeforeValues.TryGetValue(nameof(WorksheetRow.UserId), out var beforeValue);
        var hasAfter = auditEntry.AfterValues.TryGetValue(nameof(WorksheetRow.UserId), out var afterValue);
        if (!hasBefore && !hasAfter)
            return;

        var organizationId = GetGuid(entry, nameof(WorksheetRow.OrganizationId), useOriginalValue: entry.State == EntityState.Deleted)
            ?? auditEntry.OrganizationId;

        auditEntry.BeforeValues.Remove(nameof(WorksheetRow.UserId));
        auditEntry.AfterValues.Remove(nameof(WorksheetRow.UserId));

        if (hasBefore && beforeValue is Guid beforeUserId)
            auditEntry.BeforeValues[AuditFields.AssignedUser] = context.DisplayResolver.ResolveUserDisplayValue(context, organizationId, beforeUserId);

        if (hasAfter && afterValue is Guid afterUserId)
            auditEntry.AfterValues[AuditFields.AssignedUser] = context.DisplayResolver.ResolveUserDisplayValue(context, organizationId, afterUserId);
    }

    private static void SetWorksheetSummary(AuditEntry auditEntry)
    {
        var user = auditEntry.AfterValues.TryGetValue(AuditFields.AssignedUser, out var afterUser)
            ? afterUser?.ToString()
            : auditEntry.BeforeValues.TryGetValue(AuditFields.AssignedUser, out var beforeUser)
                ? beforeUser?.ToString()
                : null;

        auditEntry.Summary = auditEntry.EventType switch
        {
            AuditEventTypes.Added when !string.IsNullOrWhiteSpace(user) => string.Format(AuditSummaryTemplates.WorksheetAdded, user),
            AuditEventTypes.Deleted when !string.IsNullOrWhiteSpace(user) => string.Format(AuditSummaryTemplates.WorksheetDeleted, user),
            _ => auditEntry.Summary
        };
    }
}

internal sealed class JobReportClosureFlagAuditPolicy : IAuditEntityPolicy
{
    public bool CanHandle(EntityEntry entry) => entry.Entity is JobReportClosureFlagRow;

    public IReadOnlyList<AuditEntry> BuildEvents(AuditBuildContext context, EntityEntry entry, AuditChangeCollector collector)
    {
        if (entry.State is not (EntityState.Added or EntityState.Deleted))
            return [];

        var reportId = GetGuid(entry, nameof(JobReportClosureFlagRow.JobReportId), useOriginalValue: entry.State == EntityState.Deleted);
        if (reportId is null || !context.ProcessedClosureFlagReportIds.Add(reportId.Value))
            return [];

        var closureFlagEntries = context.DbContext.ChangeTracker.Entries<JobReportClosureFlagRow>()
            .Where(e => e.State is EntityState.Added or EntityState.Deleted)
            .Where(e => GetGuid(e, nameof(JobReportClosureFlagRow.JobReportId), useOriginalValue: e.State == EntityState.Deleted) == reportId.Value)
            .ToList();

        if (closureFlagEntries.Count == 0)
            return [];

        var auditEntry = collector.BuildBaseEntry(context, entry);
        auditEntry.BeforeValues.Clear();
        auditEntry.AfterValues.Clear();
        auditEntry.EventType = ResolveEventType(closureFlagEntries);

        var useDedicatedKeys = closureFlagEntries.Count > 1;
        foreach (var closureFlagEntry in closureFlagEntries)
        {
            var closureFlagId = GetGuid(closureFlagEntry, nameof(JobReportClosureFlagRow.ClosureFlagId), useOriginalValue: closureFlagEntry.State == EntityState.Deleted);
            if (closureFlagId is null)
                continue;

            var closureFlag = context.DisplayResolver.ResolveClosureFlagDisplayValue(context, closureFlagId.Value);
            var key = useDedicatedKeys ? $"{AuditDisplayNames.Labels[AuditFields.ClosureFlag]} / {closureFlag}" : AuditFields.ClosureFlag;

            switch (closureFlagEntry.State)
            {
                case EntityState.Added:
                    auditEntry.AfterValues[key] = closureFlag;
                    break;
                case EntityState.Deleted:
                    auditEntry.BeforeValues[key] = closureFlag;
                    break;
            }
        }

        auditEntry.Summary = auditEntry.EventType switch
        {
            AuditEventTypes.Added when !useDedicatedKeys => string.Format(AuditSummaryTemplates.ClosureFlagAdded, auditEntry.AfterValues[AuditFields.ClosureFlag]),
            AuditEventTypes.Deleted when !useDedicatedKeys => string.Format(AuditSummaryTemplates.ClosureFlagDeleted, auditEntry.BeforeValues[AuditFields.ClosureFlag]),
            AuditEventTypes.Added => AuditSummaryTemplates.ClosureFlagsAdded,
            AuditEventTypes.Deleted => AuditSummaryTemplates.ClosureFlagsDeleted,
            _ => AuditSummaryTemplates.ClosureFlagsChanged
        };

        AuditChangeCollector.Finalize(auditEntry);
        return AuditChangeCollector.ShouldSkip(auditEntry) ? [] : [auditEntry];
    }

    private static string ResolveEventType(IReadOnlyCollection<EntityEntry<JobReportClosureFlagRow>> entries)
    {
        if (entries.All(e => e.State == EntityState.Added))
            return AuditEventTypes.Added;

        if (entries.All(e => e.State == EntityState.Deleted))
            return AuditEventTypes.Deleted;

        return AuditEventTypes.Modified;
    }
}

internal sealed class JobReportInstallationAuditPolicy : IAuditEntityPolicy
{
    public bool CanHandle(EntityEntry entry) => entry.Entity is JobReportInstallationRow
        or JobReportInstallationCategoryRow
        or JobReportInstallationControlPointRow;

    private static readonly IAuditEntityPolicy CategoryPolicy = new JobReportInstallationCategoryAuditPolicy();
    private static readonly IAuditEntityPolicy ControlPointPolicy = new JobReportInstallationControlPointAuditPolicy();

    public IReadOnlyList<AuditEntry> BuildEvents(AuditBuildContext context, EntityEntry entry, AuditChangeCollector collector)
    {
        var installationId = ResolveInstallationId(entry, context.DbContext);
        if (installationId is null)
            return [];

        // Delegate child-only add/remove on existing installation to old policies
        if (ShouldDelegateToOldPolicy(context, entry, installationId.Value))
            return DelegateToOldPolicy(context, entry, collector);

        if (!context.ProcessedInstallationIds.Add(installationId.Value))
            return [];

        var consolidated = BuildConsolidatedEvent(context, entry, collector, installationId.Value);
        return consolidated is not null ? [consolidated] : [];
    }

    private static bool ShouldDelegateToOldPolicy(AuditBuildContext context, EntityEntry entry, Guid installationId)
    {
        if (entry.Entity is JobReportInstallationRow)
            return false;

        if (TryGetTrackedInstallationEntry(context.DbContext, installationId, out var installationEntry))
        {
            if (installationEntry.State is EntityState.Added or EntityState.Deleted)
                return false;

            return entry.State is EntityState.Added or EntityState.Deleted;
        }

        // Installation not tracked — query DB to see if it still exists.
        // If it does, this is a child-only modification — delegate to old policy.
        // If it doesn't, the installation is gone — consolidate as a single event.
        var existsInDb = context.DbContext.Set<JobReportInstallationRow>().AsNoTracking()
            .Any(i => i.Id == installationId);
        return existsInDb && (entry.State is EntityState.Added or EntityState.Deleted);
    }

    private static IReadOnlyList<AuditEntry> DelegateToOldPolicy(
        AuditBuildContext context, EntityEntry entry, AuditChangeCollector collector)
    {
        var oldPolicy = entry.Entity switch
        {
            JobReportInstallationCategoryRow => CategoryPolicy,
            JobReportInstallationControlPointRow => ControlPointPolicy,
            _ => null
        };

        return oldPolicy?.BuildEvents(context, entry, collector) ?? [];
    }

    private static AuditEntry? BuildConsolidatedEvent(AuditBuildContext context, EntityEntry entry, AuditChangeCollector collector, Guid installationId)
    {
        var isTracked = TryGetTrackedInstallationEntry(context.DbContext, installationId, out var installationEntry);

        var eventType = isTracked
            ? installationEntry.State switch
            {
                EntityState.Added => AuditEventTypes.Added,
                EntityState.Deleted => AuditEventTypes.Deleted,
                _ => AuditEventTypes.Modified
            }
            : entry.State switch
            {
                EntityState.Added => AuditEventTypes.Added,
                EntityState.Deleted => AuditEventTypes.Deleted,
                _ => AuditEventTypes.Modified
            };

        var baseEntry = isTracked ? installationEntry : entry;
        var auditEntry = collector.BuildBaseEntry(context, baseEntry);
        auditEntry.EventType = eventType;
        auditEntry.BeforeValues.Clear();
        auditEntry.AfterValues.Clear();

        var organizationId = isTracked
            ? GetGuid(installationEntry, nameof(JobReportInstallationRow.OrganizationId))
            : ResolveOrganizationFromDb(context, installationId);

        var installationTypeId = isTracked
            ? GetGuid(installationEntry, nameof(JobReportInstallationRow.InstallationTypeDefinitionId), installationEntry.State == EntityState.Deleted)
            : ResolveInstallationTypeIdFromDb(context, installationId);

        if (installationTypeId is null || organizationId is null)
        {
            AuditChangeCollector.Finalize(auditEntry);
            return auditEntry;
        }

        var installationTypeName = context.DisplayResolver.ResolveInstallationTypeDisplayValue(
            context, organizationId.Value, installationTypeId.Value);

        switch (eventType)
        {
            case AuditEventTypes.Deleted:
                auditEntry.BeforeValues[AuditFields.InstallationType] = installationTypeName;
                break;
            case AuditEventTypes.Modified:
                auditEntry.BeforeValues[AuditFields.InstallationType] = installationTypeName;
                auditEntry.AfterValues[AuditFields.InstallationType] = installationTypeName;
                break;
        }

        if (eventType != AuditEventTypes.Deleted)
            CollectCategoryChanges(context, auditEntry, installationId, eventType);

        auditEntry.Summary = eventType switch
        {
            AuditEventTypes.Added => string.Format(AuditSummaryTemplates.InstallationAdded, installationTypeName),
            AuditEventTypes.Deleted => string.Format(AuditSummaryTemplates.InstallationDeleted, installationTypeName),
            _ => string.Format(AuditSummaryTemplates.InstallationModified, installationTypeName)
        };

        AuditChangeCollector.Finalize(auditEntry);
        return AuditChangeCollector.ShouldSkip(auditEntry) ? null : auditEntry;
    }

    private static void CollectCategoryChanges(AuditBuildContext context, AuditEntry auditEntry, Guid installationId, string eventType)
    {
        var categoryEntries = context.DbContext.ChangeTracker.Entries<JobReportInstallationCategoryRow>()
            .Where(cat => cat.Entity.JobReportInstallationId == installationId)
            .ToList();

        foreach (var catEntry in categoryEntries)
        {
            var catId = GetGuid(catEntry, nameof(JobReportInstallationCategoryRow.ControlCategoryId),
                catEntry.State == EntityState.Deleted);
            if (catId is null) continue;

            var catName = context.DisplayResolver.ResolveControlCategoryDisplayValue(context, catId.Value);

            if (catEntry.State != EntityState.Unchanged)
            {
                var irrelevantKey = $"{catName} {AuditSuffixes.Irrelevant}";
                var irrelevantProp = catEntry.Property(nameof(JobReportInstallationCategoryRow.IsIrrelevant));

                switch (catEntry.State)
                {
                    case EntityState.Added:
                        auditEntry.AfterValues[irrelevantKey] = FormatBool(irrelevantProp.CurrentValue);
                        break;
                    case EntityState.Deleted:
                        auditEntry.BeforeValues[irrelevantKey] = FormatBool(irrelevantProp.OriginalValue);
                        break;
                    case EntityState.Modified when irrelevantProp.IsModified:
                        auditEntry.BeforeValues[irrelevantKey] = FormatBool(irrelevantProp.OriginalValue);
                        auditEntry.AfterValues[irrelevantKey] = FormatBool(irrelevantProp.CurrentValue);
                        break;
                }
            }

            CollectControlPointChanges(context, auditEntry, catEntry.Entity.Id, catName, eventType);

            if (eventType == AuditEventTypes.Added && catEntry.State == EntityState.Added)
            {
                var irrelevantKey = $"{catName} {AuditSuffixes.Irrelevant}";
                if (auditEntry.AfterValues.TryGetValue(irrelevantKey, out var irrVal) &&
                    irrVal is AuditDisplayValues.Unchecked &&
                    auditEntry.AfterValues.Keys.Any(k => k.StartsWith($"{catName}{AuditSuffixes.ControlPointSeparator}")))
                {
                    auditEntry.AfterValues.Remove(irrelevantKey);
                }
            }
        }
    }

    private static void CollectControlPointChanges(AuditBuildContext context, AuditEntry auditEntry, Guid categoryJoinId, string catName, string eventType)
    {
        var cpEntries = context.DbContext.ChangeTracker.Entries<JobReportInstallationControlPointRow>()
            .Where(cp => cp.State != EntityState.Unchanged)
            .Where(cp => cp.Entity.JobReportInstallationCategoryId == categoryJoinId)
            .ToList();

        foreach (var cpEntry in cpEntries)
        {
            var cpId = GetGuid(cpEntry, nameof(JobReportInstallationControlPointRow.ControlPointId),
                cpEntry.State == EntityState.Deleted);
            if (cpId is null) continue;

            var cpName = context.DisplayResolver.ResolveControlPointDisplayValue(context, cpId.Value);
            var key = $"{catName}{AuditSuffixes.ControlPointSeparator}{cpName}";

            var checkedProp = cpEntry.Property(nameof(JobReportInstallationControlPointRow.IsChecked));

            switch (cpEntry.State)
            {
                case EntityState.Added:
                    if (eventType == AuditEventTypes.Added && checkedProp.CurrentValue is not true)
                        break;
                    auditEntry.AfterValues[key] = FormatBool(checkedProp.CurrentValue);
                    break;
                case EntityState.Deleted:
                    auditEntry.BeforeValues[key] = FormatBool(checkedProp.OriginalValue);
                    break;
                case EntityState.Modified when checkedProp.IsModified:
                    auditEntry.BeforeValues[key] = FormatBool(checkedProp.OriginalValue);
                    auditEntry.AfterValues[key] = FormatBool(checkedProp.CurrentValue);
                    break;
            }
        }
    }

    private static string FormatBool(object? value) =>
        value is bool b ? (b ? AuditDisplayValues.Checked : AuditDisplayValues.Unchecked) : value?.ToString() ?? AuditDisplayValues.Unchecked;

    private static bool TryGetTrackedInstallationEntry(DbContext dbContext, Guid installationId, out EntityEntry<JobReportInstallationRow> entry)
    {
        foreach (var trackedEntry in dbContext.ChangeTracker.Entries<JobReportInstallationRow>())
        {
            if (trackedEntry.Entity.Id != installationId)
                continue;

            entry = trackedEntry;
            return true;
        }

        entry = null!;
        return false;
    }

    private static Guid? ResolveInstallationId(EntityEntry entry, DbContext dbContext)
    {
        if (entry.Entity is JobReportInstallationRow inst)
            return inst.Id;

        if (entry.Entity is JobReportInstallationCategoryRow cat)
            return cat.JobReportInstallationId;

        if (entry.Entity is JobReportInstallationControlPointRow cp)
        {
            foreach (var categoryEntry in dbContext.ChangeTracker.Entries<JobReportInstallationCategoryRow>())
            {
                if (categoryEntry.Entity?.Id == cp.JobReportInstallationCategoryId)
                    return categoryEntry.Entity.JobReportInstallationId;
            }

            return dbContext.Set<JobReportInstallationCategoryRow>().AsNoTracking()
                .Where(c => c.Id == cp.JobReportInstallationCategoryId)
                .Select(c => (Guid?)c.JobReportInstallationId)
                .FirstOrDefault();
        }

        return null;
    }

    private static Guid? ResolveOrganizationFromDb(AuditBuildContext context, Guid installationId) =>
        context.DbContext.Set<JobReportInstallationRow>().AsNoTracking()
            .Where(i => i.Id == installationId)
            .Select(i => (Guid?)i.OrganizationId)
            .FirstOrDefault();

    private static Guid? ResolveInstallationTypeIdFromDb(AuditBuildContext context, Guid installationId) =>
        context.DbContext.Set<JobReportInstallationRow>().AsNoTracking()
            .Where(i => i.Id == installationId)
            .Select(i => (Guid?)i.InstallationTypeDefinitionId)
            .FirstOrDefault();
}

internal sealed class JobReportInstallationCategoryAuditPolicy : IAuditEntityPolicy
{
    public bool CanHandle(EntityEntry entry) => entry.Entity is JobReportInstallationCategoryRow;

    public IReadOnlyList<AuditEntry> BuildEvents(AuditBuildContext context, EntityEntry entry, AuditChangeCollector collector)
    {
        if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            return [];

        // Suppress Added/Deleted when parent installation type is also added/deleted in same save
        if (entry.State is EntityState.Added or EntityState.Deleted)
        {
            var parentId = entry.Property(nameof(JobReportInstallationCategoryRow.JobReportInstallationId)).CurrentValue;
            if (parentId is Guid parentGuid && context.DbContext.ChangeTracker.Entries<JobReportInstallationRow>()
                .Any(e => e.State == entry.State && e.Entity.Id == parentGuid))
                return [];
        }

        var auditEntry = collector.BuildBaseEntry(context, entry);
        var categoryId = GetGuid(entry, nameof(JobReportInstallationCategoryRow.ControlCategoryId), useOriginalValue: entry.State == EntityState.Deleted)
            ?? GetGuid(entry, nameof(JobReportInstallationCategoryRow.ControlCategoryId), useOriginalValue: true);
        if (categoryId is null)
            return [];

        var category = context.DisplayResolver.ResolveControlCategoryDisplayValue(context, categoryId.Value);
        var hasIrrelevantChange = auditEntry.BeforeValues.ContainsKey(nameof(JobReportInstallationCategoryRow.IsIrrelevant))
            || auditEntry.AfterValues.ContainsKey(nameof(JobReportInstallationCategoryRow.IsIrrelevant));
        auditEntry.BeforeValues.Clear();
        auditEntry.AfterValues.Clear();

        switch (auditEntry.EventType)
        {
            case AuditEventTypes.Added:
                auditEntry.AfterValues[AuditFields.InstallationCategory] = category;
                auditEntry.AfterValues[nameof(JobReportInstallationCategoryRow.IsIrrelevant)] = entry.Property(nameof(JobReportInstallationCategoryRow.IsIrrelevant)).CurrentValue;
                {
                    var instType = ResolveInstallationTypeName(context, entry);
                    if (instType is not null)
                        auditEntry.AfterValues[AuditFields.InstallationType] = instType;
                    auditEntry.Summary = instType is not null
                        ? string.Format(AuditSummaryTemplates.CategoryAddedWithType, category, instType)
                        : string.Format(AuditSummaryTemplates.CategoryAdded, category);
                }
                break;

            case AuditEventTypes.Deleted:
                auditEntry.BeforeValues[AuditFields.InstallationCategory] = category;
                auditEntry.BeforeValues[nameof(JobReportInstallationCategoryRow.IsIrrelevant)] = entry.Property(nameof(JobReportInstallationCategoryRow.IsIrrelevant)).OriginalValue;
                {
                    var instType = ResolveInstallationTypeName(context, entry);
                    if (instType is not null)
                        auditEntry.BeforeValues[AuditFields.InstallationType] = instType;
                    auditEntry.Summary = instType is not null
                        ? string.Format(AuditSummaryTemplates.CategoryDeletedWithType, category, instType)
                        : string.Format(AuditSummaryTemplates.CategoryDeleted, category);
                }
                break;

            case AuditEventTypes.Modified:
                if (!hasIrrelevantChange)
                    return [];

                auditEntry.BeforeValues[nameof(JobReportInstallationCategoryRow.IsIrrelevant)] = entry.Property(nameof(JobReportInstallationCategoryRow.IsIrrelevant)).OriginalValue;
                auditEntry.AfterValues[nameof(JobReportInstallationCategoryRow.IsIrrelevant)] = entry.Property(nameof(JobReportInstallationCategoryRow.IsIrrelevant)).CurrentValue;
                auditEntry.AfterValues[AuditFields.InstallationCategory] = category;
                {
                    var instType = ResolveInstallationTypeName(context, entry);
                    if (instType is not null)
                    {
                        auditEntry.BeforeValues[AuditFields.InstallationType] = instType;
                        auditEntry.AfterValues[AuditFields.InstallationType] = instType;
                    }
                    auditEntry.Summary = instType is not null
                        ? string.Format(AuditSummaryTemplates.CategoryModifiedWithType, category, instType)
                        : string.Format(AuditSummaryTemplates.CategoryModified, category);
                }
                break;
        }

        AuditChangeCollector.Finalize(auditEntry);
        return [auditEntry];
    }

    private static string? ResolveInstallationTypeName(AuditBuildContext context, EntityEntry entry)
    {
        var installationId = GetGuid(entry, nameof(JobReportInstallationCategoryRow.JobReportInstallationId));
        if (installationId is null) return null;

        var installation = context.DbContext.Set<JobReportInstallationRow>().Local
            .FirstOrDefault(i => i.Id == installationId.Value)
            ?? context.DbContext.Set<JobReportInstallationRow>().AsNoTracking()
                .FirstOrDefault(i => i.Id == installationId.Value);

        if (installation is null) return null;

        return context.DisplayResolver.ResolveInstallationTypeDisplayValue(
            context, installation.OrganizationId, installation.InstallationTypeDefinitionId);
    }
}

internal sealed class JobReportInstallationControlPointAuditPolicy : IAuditEntityPolicy
{
    public bool CanHandle(EntityEntry entry) => entry.Entity is JobReportInstallationControlPointRow;

    public IReadOnlyList<AuditEntry> BuildEvents(AuditBuildContext context, EntityEntry entry, AuditChangeCollector collector)
    {
        if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            return [];

        // Suppress Added/Deleted when the ultimate parent installation type row is also added/deleted
        if (entry.State is EntityState.Added or EntityState.Deleted)
        {
            var parentCategoryId = entry.Property(nameof(JobReportInstallationControlPointRow.JobReportInstallationCategoryId)).CurrentValue;
            if (parentCategoryId is Guid pgId)
            {
                var parentCategoryEntry = context.DbContext.ChangeTracker.Entries<JobReportInstallationCategoryRow>()
                    .FirstOrDefault(e => e.State == entry.State && e.Entity.Id == pgId);
                if (parentCategoryEntry is not null)
                {
                    var grandparentInstallationId = parentCategoryEntry
                        .Property(nameof(JobReportInstallationCategoryRow.JobReportInstallationId)).CurrentValue;
                    if (grandparentInstallationId is Guid gpId && context.DbContext.ChangeTracker.Entries<JobReportInstallationRow>()
                        .Any(e => e.State == entry.State && e.Entity.Id == gpId))
                        return [];
                }
            }
        }

        var auditEntry = collector.BuildBaseEntry(context, entry);
        var controlPointId = GetGuid(entry, nameof(JobReportInstallationControlPointRow.ControlPointId), useOriginalValue: entry.State == EntityState.Deleted)
            ?? GetGuid(entry, nameof(JobReportInstallationControlPointRow.ControlPointId), useOriginalValue: true);
        if (controlPointId is null)
            return [];

        var controlPoint = context.DisplayResolver.ResolveControlPointDisplayValue(context, controlPointId.Value);
        var hasCheckedChange = auditEntry.BeforeValues.ContainsKey(nameof(JobReportInstallationControlPointRow.IsChecked))
            || auditEntry.AfterValues.ContainsKey(nameof(JobReportInstallationControlPointRow.IsChecked));
        auditEntry.BeforeValues.Clear();
        auditEntry.AfterValues.Clear();

        switch (auditEntry.EventType)
        {
            case AuditEventTypes.Added:
                auditEntry.AfterValues[AuditFields.ControlPoint] = controlPoint;
                auditEntry.AfterValues[nameof(JobReportInstallationControlPointRow.IsChecked)] = FormatJaNej(entry.Property(nameof(JobReportInstallationControlPointRow.IsChecked)).CurrentValue);
                {
                    var ctx = ResolveControlPointParentContext(context, entry);
                    if (ctx.InstallationType is not null)
                        auditEntry.AfterValues[AuditFields.InstallationType] = ctx.InstallationType;
                    if (ctx.CategoryName is not null)
                        auditEntry.AfterValues[AuditFields.InstallationCategory] = ctx.CategoryName;
                    auditEntry.Summary = BuildControlPointSummary(controlPoint, ctx, "tilføjet");
                }
                break;

            case AuditEventTypes.Deleted:
                auditEntry.BeforeValues[AuditFields.ControlPoint] = controlPoint;
                auditEntry.BeforeValues[nameof(JobReportInstallationControlPointRow.IsChecked)] = FormatJaNej(entry.Property(nameof(JobReportInstallationControlPointRow.IsChecked)).OriginalValue);
                {
                    var ctx = ResolveControlPointParentContext(context, entry);
                    if (ctx.InstallationType is not null)
                        auditEntry.BeforeValues[AuditFields.InstallationType] = ctx.InstallationType;
                    if (ctx.CategoryName is not null)
                        auditEntry.BeforeValues[AuditFields.InstallationCategory] = ctx.CategoryName;
                    auditEntry.Summary = BuildControlPointSummary(controlPoint, ctx, "fjernet");
                }
                break;

            case AuditEventTypes.Modified:
                if (!hasCheckedChange)
                    return [];

                auditEntry.BeforeValues[nameof(JobReportInstallationControlPointRow.IsChecked)] = FormatJaNej(entry.Property(nameof(JobReportInstallationControlPointRow.IsChecked)).OriginalValue);
                auditEntry.AfterValues[nameof(JobReportInstallationControlPointRow.IsChecked)] = FormatJaNej(entry.Property(nameof(JobReportInstallationControlPointRow.IsChecked)).CurrentValue);
                auditEntry.AfterValues[AuditFields.ControlPoint] = controlPoint;
                {
                    var ctx = ResolveControlPointParentContext(context, entry);
                    if (ctx.InstallationType is not null)
                    {
                        auditEntry.BeforeValues[AuditFields.InstallationType] = ctx.InstallationType;
                        auditEntry.AfterValues[AuditFields.InstallationType] = ctx.InstallationType;
                    }
                    if (ctx.CategoryName is not null)
                    {
                        auditEntry.BeforeValues[AuditFields.InstallationCategory] = ctx.CategoryName;
                        auditEntry.AfterValues[AuditFields.InstallationCategory] = ctx.CategoryName;
                    }
                    auditEntry.Summary = BuildControlPointSummary(controlPoint, ctx, "ændret");
                }
                break;
        }

        AuditChangeCollector.Finalize(auditEntry);
        return [auditEntry];
    }

    private static (string? CategoryName, string? InstallationType) ResolveControlPointParentContext(AuditBuildContext context, EntityEntry entry)
    {
        var categoryJoinId = GetGuid(entry, nameof(JobReportInstallationControlPointRow.JobReportInstallationCategoryId));
        if (categoryJoinId is null) return (null, null);

        var category = context.DbContext.Set<JobReportInstallationCategoryRow>().Local
            .FirstOrDefault(c => c.Id == categoryJoinId.Value)
            ?? context.DbContext.Set<JobReportInstallationCategoryRow>().AsNoTracking()
                .FirstOrDefault(c => c.Id == categoryJoinId.Value);

        if (category is null) return (null, null);

        var categoryName = context.DisplayResolver.ResolveControlCategoryDisplayValue(context, category.ControlCategoryId);

        var installation = context.DbContext.Set<JobReportInstallationRow>().Local
            .FirstOrDefault(i => i.Id == category.JobReportInstallationId)
            ?? context.DbContext.Set<JobReportInstallationRow>().AsNoTracking()
                .FirstOrDefault(i => i.Id == category.JobReportInstallationId);

        var installationType = installation is not null
            ? context.DisplayResolver.ResolveInstallationTypeDisplayValue(context, installation.OrganizationId, installation.InstallationTypeDefinitionId)
            : null;

        return (categoryName, installationType);
    }

    private static string BuildControlPointSummary(string controlPoint, (string? CategoryName, string? InstallationType) ctx, string action)
    {
        var parts = new List<string> { string.Format(AuditSummaryTemplates.ControlPointLabel, controlPoint) };
        if (ctx.CategoryName is not null && ctx.InstallationType is not null)
            parts.Add(string.Format(AuditSummaryTemplates.OnCategoryAndType, ctx.CategoryName, ctx.InstallationType));
        else if (ctx.InstallationType is not null)
            parts.Add(string.Format(AuditSummaryTemplates.OnType, ctx.InstallationType));
        else if (ctx.CategoryName is not null)
            parts.Add(string.Format(AuditSummaryTemplates.OnType, ctx.CategoryName));
        parts.Add(action);
        return string.Join(" ", parts);
    }

    private static string FormatJaNej(object? value) =>
        value is bool b ? (b ? AuditDisplayValues.Checked : AuditDisplayValues.Unchecked) : value?.ToString() ?? AuditDisplayValues.Unchecked;
}

internal sealed class JobReportLinkAuditPolicy : IAuditEntityPolicy
{
    public bool CanHandle(EntityEntry entry) => entry.Entity is JobReportLinkRow;

    public IReadOnlyList<AuditEntry> BuildEvents(AuditBuildContext context, EntityEntry entry, AuditChangeCollector collector)
    {
        if (entry.Entity is not JobReportLinkRow || entry.State is not (EntityState.Added or EntityState.Deleted))
            return [];

        var linkEntries = context.DbContext.ChangeTracker.Entries<JobReportLinkRow>()
            .Where(e => e.State is EntityState.Added or EntityState.Deleted)
            .ToList();

        var perspectiveReportIds = linkEntries
            .SelectMany(GetReportIds)
            .Distinct()
            .Where(context.ProcessedLinkReportIds.Add)
            .ToArray();

        if (perspectiveReportIds.Length == 0)
            return [];

        var events = new List<AuditEntry>();
        foreach (var perspectiveReportId in perspectiveReportIds)
        {
            var relevantEntries = linkEntries
                .Where(linkEntry => GetReportIds(linkEntry).Contains(perspectiveReportId))
                .ToArray();

            if (relevantEntries.Length == 0)
                continue;

            var auditEntry = collector.BuildBaseEntry(context, relevantEntries[0]);
            auditEntry.ReportId = perspectiveReportId;
            auditEntry.EventType = ResolveEventType(relevantEntries);
            EnrichJobLinks(context, auditEntry, relevantEntries, perspectiveReportId);
            AuditChangeCollector.Finalize(auditEntry);

            if (!AuditChangeCollector.ShouldSkip(auditEntry))
                events.Add(auditEntry);
        }

        return events;
    }

    private static void EnrichJobLinks(AuditBuildContext context, AuditEntry auditEntry, IReadOnlyList<EntityEntry<JobReportLinkRow>> entries, Guid perspectiveReportId)
    {
        auditEntry.BeforeValues.Clear();
        auditEntry.AfterValues.Clear();

        var useDedicatedKeys = entries.Count > 1;
        foreach (var entry in entries)
        {
            var sourceReportId = GetGuid(entry, nameof(JobReportLinkRow.SourceReportId), useOriginalValue: entry.State == EntityState.Deleted);
            var targetReportId = GetGuid(entry, nameof(JobReportLinkRow.TargetReportId), useOriginalValue: entry.State == EntityState.Deleted);
            if (sourceReportId is null || targetReportId is null)
                continue;

            var linkedReportId = perspectiveReportId == sourceReportId.Value
                ? targetReportId.Value
                : sourceReportId.Value;
            var linkedReport = context.DisplayResolver.ResolveReportDisplayValue(context, auditEntry.OrganizationId, linkedReportId);
            var key = useDedicatedKeys ? $"{AuditDisplayNames.Labels[AuditFields.LinkedReport]} / {linkedReport}" : AuditFields.LinkedReport;

            switch (entry.State)
            {
                case EntityState.Added:
                    auditEntry.AfterValues[key] = linkedReport;
                    break;

                case EntityState.Deleted:
                    auditEntry.BeforeValues[key] = linkedReport;
                    break;
            }
        }

        auditEntry.Summary = auditEntry.EventType switch
        {
            AuditEventTypes.Added when !useDedicatedKeys => string.Format(AuditSummaryTemplates.LinkAdded, auditEntry.AfterValues[AuditFields.LinkedReport]),
            AuditEventTypes.Deleted when !useDedicatedKeys => string.Format(AuditSummaryTemplates.LinkDeleted, auditEntry.BeforeValues[AuditFields.LinkedReport]),
            AuditEventTypes.Added => AuditSummaryTemplates.LinksAdded,
            AuditEventTypes.Deleted => AuditSummaryTemplates.LinksDeleted,
            _ => AuditSummaryTemplates.LinksChanged
        };
    }

    private static string ResolveEventType(IReadOnlyCollection<EntityEntry<JobReportLinkRow>> entries)
    {
        if (entries.All(e => e.State == EntityState.Added))
            return AuditEventTypes.Added;

        if (entries.All(e => e.State == EntityState.Deleted))
            return AuditEventTypes.Deleted;

        return AuditEventTypes.Modified;
    }

    private static IEnumerable<Guid> GetReportIds(EntityEntry<JobReportLinkRow> entry)
    {
        var sourceReportId = GetGuid(entry, nameof(JobReportLinkRow.SourceReportId), useOriginalValue: entry.State == EntityState.Deleted);
        var targetReportId = GetGuid(entry, nameof(JobReportLinkRow.TargetReportId), useOriginalValue: entry.State == EntityState.Deleted);

        if (sourceReportId is not null)
            yield return sourceReportId.Value;

        if (targetReportId is not null)
            yield return targetReportId.Value;
    }
}
