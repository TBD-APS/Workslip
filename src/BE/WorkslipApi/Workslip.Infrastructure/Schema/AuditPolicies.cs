using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Workslip.Domain.Models;

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
            auditEntry.BeforeValues[AuditFields.WorkKind] = context.DisplayResolver.ResolveWorkKindDisplayValue(beforeValue as Guid?, context.DbContext);

        if (hasAfter)
            auditEntry.AfterValues[AuditFields.WorkKind] = context.DisplayResolver.ResolveWorkKindDisplayValue(afterValue as Guid?, context.DbContext);
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
            auditEntry.BeforeValues[AuditFields.Customer] = context.DisplayResolver.ResolveCustomerDisplayValue(organizationId, beforeValue as Guid?, context.DbContext);

        if (hasAfter)
            auditEntry.AfterValues[AuditFields.Customer] = context.DisplayResolver.ResolveCustomerDisplayValue(organizationId, afterValue as Guid?, context.DbContext);
    }

    private static Guid? GetGuid(EntityEntry entry, string propertyName, bool useOriginalValue = false)
    {
        var property = entry.Property(propertyName);
        var value = useOriginalValue ? property.OriginalValue : property.CurrentValue;
        return value is Guid id ? id : null;
    }
}

internal sealed class JobAssignmentAuditPolicy : IAuditEntityPolicy
{
    public bool CanHandle(EntityEntry entry) => entry.Entity is JobAssignmentRow;

    public IReadOnlyList<AuditEntry> BuildEvents(AuditBuildContext context, EntityEntry entry, AuditChangeCollector collector)
    {
        var auditEntry = collector.BuildBaseEntry(context, entry);
        var organizationId = GetGuid(entry, nameof(JobAssignmentRow.OrganizationId)) ?? auditEntry.OrganizationId;
        var beforeUserId = GetGuid(entry, nameof(JobAssignmentRow.UserId), useOriginalValue: true);
        var afterUserId = GetGuid(entry, nameof(JobAssignmentRow.UserId));
        var beforeUser = beforeUserId is null
            ? null
            : context.DisplayResolver.ResolveUserDisplayValue(organizationId, beforeUserId.Value, context.DbContext);
        var afterUser = afterUserId is null
            ? null
            : context.DisplayResolver.ResolveUserDisplayValue(organizationId, afterUserId.Value, context.DbContext);

        auditEntry.BeforeValues.Clear();
        auditEntry.AfterValues.Clear();

        switch (auditEntry.EventType)
        {
            case AuditEventTypes.Added:
                auditEntry.AfterValues[AuditFields.AssignedUser] = afterUser;
                auditEntry.Summary = $"{afterUser} assigned";
                break;

            case AuditEventTypes.Deleted:
                auditEntry.BeforeValues[AuditFields.AssignedUser] = beforeUser;
                auditEntry.Summary = $"{beforeUser} unassigned";
                break;

            case AuditEventTypes.Modified when beforeUser != afterUser:
                auditEntry.BeforeValues[AuditFields.AssignedUser] = beforeUser;
                auditEntry.AfterValues[AuditFields.AssignedUser] = afterUser;
                auditEntry.Summary = $"Assignment changed: '{beforeUser}' → '{afterUser}'";
                break;
        }

        AuditChangeCollector.Finalize(auditEntry);
        return AuditChangeCollector.ShouldSkip(auditEntry) ? [] : [auditEntry];
    }

    private static Guid? GetGuid(EntityEntry entry, string propertyName, bool useOriginalValue = false)
    {
        var property = entry.Property(propertyName);
        var value = useOriginalValue ? property.OriginalValue : property.CurrentValue;
        return value is Guid id ? id : null;
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
            auditEntry.BeforeValues[AuditFields.Report] = context.DisplayResolver.ResolveReportDisplayValue(organizationId, beforeReportId, context.DbContext);

        if (hasAfter && afterValue is Guid afterReportId)
            auditEntry.AfterValues[AuditFields.Report] = context.DisplayResolver.ResolveReportDisplayValue(organizationId, afterReportId, context.DbContext);
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
            auditEntry.BeforeValues[AuditFields.AssignedUser] = context.DisplayResolver.ResolveUserDisplayValue(organizationId, beforeUserId, context.DbContext);

        if (hasAfter && afterValue is Guid afterUserId)
            auditEntry.AfterValues[AuditFields.AssignedUser] = context.DisplayResolver.ResolveUserDisplayValue(organizationId, afterUserId, context.DbContext);
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
            AuditEventTypes.Added when !string.IsNullOrWhiteSpace(user) => $"Worksheet for {user} added",
            AuditEventTypes.Deleted when !string.IsNullOrWhiteSpace(user) => $"Worksheet for {user} removed",
            _ => auditEntry.Summary
        };
    }

    private static Guid? GetGuid(EntityEntry entry, string propertyName, bool useOriginalValue = false)
    {
        var property = entry.Property(propertyName);
        var value = useOriginalValue ? property.OriginalValue : property.CurrentValue;
        return value is Guid id ? id : null;
    }
}

internal sealed class JobReportClosureFlagAuditPolicy : IAuditEntityPolicy
{
    public bool CanHandle(EntityEntry entry) => entry.Entity is JobReportClosureFlagRow;

    public IReadOnlyList<AuditEntry> BuildEvents(AuditBuildContext context, EntityEntry entry, AuditChangeCollector collector)
    {
        if (entry.State is not (EntityState.Added or EntityState.Deleted))
            return [];

        var auditEntry = collector.BuildBaseEntry(context, entry);
        var closureFlagId = GetGuid(entry, nameof(JobReportClosureFlagRow.ClosureFlagId), useOriginalValue: entry.State == EntityState.Deleted);
        if (closureFlagId is null)
            return [];

        var closureFlag = context.DisplayResolver.ResolveClosureFlagDisplayValue(closureFlagId.Value, context.DbContext);
        auditEntry.BeforeValues.Clear();
        auditEntry.AfterValues.Clear();

        switch (auditEntry.EventType)
        {
            case AuditEventTypes.Added:
                auditEntry.AfterValues[AuditFields.ClosureFlag] = closureFlag;
                auditEntry.Summary = $"Closure flag {closureFlag} added";
                break;

            case AuditEventTypes.Deleted:
                auditEntry.BeforeValues[AuditFields.ClosureFlag] = closureFlag;
                auditEntry.Summary = $"Closure flag {closureFlag} removed";
                break;
        }

        AuditChangeCollector.Finalize(auditEntry);
        return [auditEntry];
    }

    private static Guid? GetGuid(EntityEntry entry, string propertyName, bool useOriginalValue = false)
    {
        var property = entry.Property(propertyName);
        var value = useOriginalValue ? property.OriginalValue : property.CurrentValue;
        return value is Guid id ? id : null;
    }
}

internal sealed class JobReportInstallationAuditPolicy : IAuditEntityPolicy
{
    public bool CanHandle(EntityEntry entry) => entry.Entity is JobReportInstallationRow;

    public IReadOnlyList<AuditEntry> BuildEvents(AuditBuildContext context, EntityEntry entry, AuditChangeCollector collector)
    {
        var auditEntry = collector.BuildBaseEntry(context, entry);
        var installationTypeId = GetGuid(entry, nameof(JobReportInstallationRow.InstallationTypeDefinitionId), useOriginalValue: entry.State == EntityState.Deleted);
        if (installationTypeId is null)
            return [];

        var installationType = context.DisplayResolver.ResolveInstallationTypeDisplayValue(auditEntry.OrganizationId, installationTypeId.Value, context.DbContext);
        auditEntry.BeforeValues.Clear();
        auditEntry.AfterValues.Clear();

        switch (auditEntry.EventType)
        {
            case AuditEventTypes.Added:
                auditEntry.AfterValues[AuditFields.InstallationType] = installationType;
                auditEntry.Summary = $"Installation type {installationType} added";
                break;

            case AuditEventTypes.Deleted:
                auditEntry.BeforeValues[AuditFields.InstallationType] = installationType;
                auditEntry.Summary = $"Installation type {installationType} removed";
                break;

            default:
                return [];
        }

        AuditChangeCollector.Finalize(auditEntry);
        return [auditEntry];
    }

    private static Guid? GetGuid(EntityEntry entry, string propertyName, bool useOriginalValue = false)
    {
        var property = entry.Property(propertyName);
        var value = useOriginalValue ? property.OriginalValue : property.CurrentValue;
        return value is Guid id ? id : null;
    }
}

internal sealed class JobReportInstallationCategoryAuditPolicy : IAuditEntityPolicy
{
    public bool CanHandle(EntityEntry entry) => entry.Entity is JobReportInstallationCategoryRow;

    public IReadOnlyList<AuditEntry> BuildEvents(AuditBuildContext context, EntityEntry entry, AuditChangeCollector collector)
    {
        if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            return [];

        var auditEntry = collector.BuildBaseEntry(context, entry);
        var categoryId = GetGuid(entry, nameof(JobReportInstallationCategoryRow.ControlCategoryId), useOriginalValue: entry.State == EntityState.Deleted)
            ?? GetGuid(entry, nameof(JobReportInstallationCategoryRow.ControlCategoryId), useOriginalValue: true);
        if (categoryId is null)
            return [];

        var category = context.DisplayResolver.ResolveControlCategoryDisplayValue(categoryId.Value, context.DbContext);
        var hasIrrelevantChange = auditEntry.BeforeValues.ContainsKey(nameof(JobReportInstallationCategoryRow.IsIrrelevant))
            || auditEntry.AfterValues.ContainsKey(nameof(JobReportInstallationCategoryRow.IsIrrelevant));
        auditEntry.BeforeValues.Clear();
        auditEntry.AfterValues.Clear();

        switch (auditEntry.EventType)
        {
            case AuditEventTypes.Added:
                auditEntry.AfterValues[AuditFields.InstallationCategory] = category;
                auditEntry.AfterValues[nameof(JobReportInstallationCategoryRow.IsIrrelevant)] = entry.Property(nameof(JobReportInstallationCategoryRow.IsIrrelevant)).CurrentValue;
                auditEntry.Summary = $"Category {category} added";
                break;

            case AuditEventTypes.Deleted:
                auditEntry.BeforeValues[AuditFields.InstallationCategory] = category;
                auditEntry.BeforeValues[nameof(JobReportInstallationCategoryRow.IsIrrelevant)] = entry.Property(nameof(JobReportInstallationCategoryRow.IsIrrelevant)).OriginalValue;
                auditEntry.Summary = $"Category {category} removed";
                break;

            case AuditEventTypes.Modified:
                if (!hasIrrelevantChange)
                    return [];

                auditEntry.BeforeValues[nameof(JobReportInstallationCategoryRow.IsIrrelevant)] = entry.Property(nameof(JobReportInstallationCategoryRow.IsIrrelevant)).OriginalValue;
                auditEntry.AfterValues[nameof(JobReportInstallationCategoryRow.IsIrrelevant)] = entry.Property(nameof(JobReportInstallationCategoryRow.IsIrrelevant)).CurrentValue;
                auditEntry.AfterValues[AuditFields.InstallationCategory] = category;
                auditEntry.Summary = $"Category {category} relevance changed";
                break;
        }

        AuditChangeCollector.Finalize(auditEntry);
        return [auditEntry];
    }

    private static Guid? GetGuid(EntityEntry entry, string propertyName, bool useOriginalValue = false)
    {
        var property = entry.Property(propertyName);
        var value = useOriginalValue ? property.OriginalValue : property.CurrentValue;
        return value is Guid id ? id : null;
    }
}

internal sealed class JobReportInstallationControlPointAuditPolicy : IAuditEntityPolicy
{
    public bool CanHandle(EntityEntry entry) => entry.Entity is JobReportInstallationControlPointRow;

    public IReadOnlyList<AuditEntry> BuildEvents(AuditBuildContext context, EntityEntry entry, AuditChangeCollector collector)
    {
        if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            return [];

        var auditEntry = collector.BuildBaseEntry(context, entry);
        var controlPointId = GetGuid(entry, nameof(JobReportInstallationControlPointRow.ControlPointId), useOriginalValue: entry.State == EntityState.Deleted)
            ?? GetGuid(entry, nameof(JobReportInstallationControlPointRow.ControlPointId), useOriginalValue: true);
        if (controlPointId is null)
            return [];

        var controlPoint = context.DisplayResolver.ResolveControlPointDisplayValue(controlPointId.Value, context.DbContext);
        var hasCheckedChange = auditEntry.BeforeValues.ContainsKey(nameof(JobReportInstallationControlPointRow.IsChecked))
            || auditEntry.AfterValues.ContainsKey(nameof(JobReportInstallationControlPointRow.IsChecked));
        auditEntry.BeforeValues.Clear();
        auditEntry.AfterValues.Clear();

        switch (auditEntry.EventType)
        {
            case AuditEventTypes.Added:
                auditEntry.AfterValues[AuditFields.ControlPoint] = controlPoint;
                auditEntry.AfterValues[nameof(JobReportInstallationControlPointRow.IsChecked)] = entry.Property(nameof(JobReportInstallationControlPointRow.IsChecked)).CurrentValue;
                auditEntry.Summary = $"Control point {controlPoint} added";
                break;

            case AuditEventTypes.Deleted:
                auditEntry.BeforeValues[AuditFields.ControlPoint] = controlPoint;
                auditEntry.BeforeValues[nameof(JobReportInstallationControlPointRow.IsChecked)] = entry.Property(nameof(JobReportInstallationControlPointRow.IsChecked)).OriginalValue;
                auditEntry.Summary = $"Control point {controlPoint} removed";
                break;

            case AuditEventTypes.Modified:
                if (!hasCheckedChange)
                    return [];

                auditEntry.BeforeValues[nameof(JobReportInstallationControlPointRow.IsChecked)] = entry.Property(nameof(JobReportInstallationControlPointRow.IsChecked)).OriginalValue;
                auditEntry.AfterValues[nameof(JobReportInstallationControlPointRow.IsChecked)] = entry.Property(nameof(JobReportInstallationControlPointRow.IsChecked)).CurrentValue;
                auditEntry.AfterValues[AuditFields.ControlPoint] = controlPoint;
                auditEntry.Summary = $"Control point {controlPoint} changed";
                break;
        }

        AuditChangeCollector.Finalize(auditEntry);
        return [auditEntry];
    }

    private static Guid? GetGuid(EntityEntry entry, string propertyName, bool useOriginalValue = false)
    {
        var property = entry.Property(propertyName);
        var value = useOriginalValue ? property.OriginalValue : property.CurrentValue;
        return value is Guid id ? id : null;
    }
}

internal sealed class JobReportLinkAuditPolicy : IAuditEntityPolicy
{
    public bool CanHandle(EntityEntry entry) => entry.Entity is JobReportLinkRow;

    public IReadOnlyList<AuditEntry> BuildEvents(AuditBuildContext context, EntityEntry entry, AuditChangeCollector collector)
    {
        if (entry.Entity is not JobReportLinkRow link || entry.State is not (EntityState.Added or EntityState.Deleted))
            return [];

        var sourceAuditEntry = collector.BuildBaseEntry(context, entry);
        sourceAuditEntry.ReportId = link.SourceReportId;
        EnrichJobLink(context, entry, sourceAuditEntry, link.SourceReportId);
        AuditChangeCollector.Finalize(sourceAuditEntry);

        var targetAuditEntry = sourceAuditEntry.Clone();
        targetAuditEntry.ReportId = link.TargetReportId;
        EnrichJobLink(context, entry, targetAuditEntry, link.TargetReportId);
        AuditChangeCollector.Finalize(targetAuditEntry);

        return [sourceAuditEntry, targetAuditEntry];
    }

    private static void EnrichJobLink(AuditBuildContext context, EntityEntry entry, AuditEntry auditEntry, Guid perspectiveReportId)
    {
        var sourceReportId = GetGuid(entry, nameof(JobReportLinkRow.SourceReportId), useOriginalValue: entry.State == EntityState.Deleted);
        var targetReportId = GetGuid(entry, nameof(JobReportLinkRow.TargetReportId), useOriginalValue: entry.State == EntityState.Deleted);
        if (sourceReportId is null || targetReportId is null)
            return;

        var linkedReportId = perspectiveReportId == sourceReportId.Value
            ? targetReportId.Value
            : sourceReportId.Value;
        var linkedReport = context.DisplayResolver.ResolveReportDisplayValue(auditEntry.OrganizationId, linkedReportId, context.DbContext);

        auditEntry.BeforeValues.Clear();
        auditEntry.AfterValues.Clear();

        switch (auditEntry.EventType)
        {
            case AuditEventTypes.Added:
                auditEntry.AfterValues[AuditFields.LinkedReport] = linkedReport;
                auditEntry.Summary = $"Link to {linkedReport} added";
                break;

            case AuditEventTypes.Deleted:
                auditEntry.BeforeValues[AuditFields.LinkedReport] = linkedReport;
                auditEntry.Summary = $"Link to {linkedReport} removed";
                break;
        }
    }

    private static Guid? GetGuid(EntityEntry entry, string propertyName, bool useOriginalValue = false)
    {
        var property = entry.Property(propertyName);
        var value = useOriginalValue ? property.OriginalValue : property.CurrentValue;
        return value is Guid id ? id : null;
    }
}
