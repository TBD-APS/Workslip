using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Workslip.Application.Auth;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

public sealed class AuditInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static readonly AsyncLocal<bool> IsSaving = new();

    private readonly ICurrentUserContext currentUser;
    private readonly AuditDisplayResolver displayResolver = new();
    private readonly AuditChangeCollector changeCollector = new();
    private readonly IReadOnlyList<IAuditEntityPolicy> policies =
    [
        new JobReportAuditPolicy(),
        new WorksheetAuditPolicy(),
        new JobAssignmentAuditPolicy(),
        new JobReportLinkAuditPolicy(),
        new JobReportClosureFlagAuditPolicy(),
        new JobReportInstallationAuditPolicy(),
        new JobReportInstallationCategoryAuditPolicy(),
        new JobReportInstallationControlPointAuditPolicy(),
        new DefaultAuditPolicy()
    ];

    public AuditInterceptor(ICurrentUserContext currentUser)
    {
        this.currentUser = currentUser;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (IsSaving.Value)
            return result;

        IsSaving.Value = true;
        try
        {
            var dbContext = eventData.Context;
            if (dbContext is null) return result;

            var auditEntries = OnBeforeSaveChanges(dbContext);

            if (auditEntries.Count > 0)
            {
                var createdAt = DateTimeOffset.UtcNow;
                var eventRows = auditEntries.Select(ae => new JobEventRow
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = ae.OrganizationId,
                    ReportId = ae.ReportId,
                    ActorId = TenantActorPolicy.ResolveTenantUserReference(ae.ActorId, currentUser.Role),
                    EventType = ae.EventType,
                    Summary = ae.Summary,
                    BeforeJson = ae.BeforeValues.Count > 0 ? JsonSerializer.Serialize(ae.BeforeValues, JsonOptions) : null,
                    AfterJson = ae.AfterValues.Count > 0 ? JsonSerializer.Serialize(ae.AfterValues, JsonOptions) : null,
                    CreatedAt = createdAt
                });

                dbContext.Set<JobEventRow>().AddRange(eventRows);
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
        finally
        {
            IsSaving.Value = false;
        }
    }

    private List<AuditEntry> OnBeforeSaveChanges(DbContext dbContext)
    {
        if (dbContext is SqlDbContext sqlContext && sqlContext.IsSeeding)
            return [];

        dbContext.ChangeTracker.DetectChanges();
        var auditEntries = new List<AuditEntry>();
        var buildContext = new AuditBuildContext(dbContext, currentUser, displayResolver);
        var reportsWithWorkKindChange = ReportsWithWorkKindChange(dbContext);

        foreach (var entry in dbContext.ChangeTracker.Entries())
        {
            if (entry.Entity is JobEventRow || entry.State is EntityState.Detached or EntityState.Unchanged)
                continue;

            if (entry.Entity is not IAuditable)
                continue;

            var policy = policies.First(x => x.CanHandle(entry));
            auditEntries.AddRange(policy
                .BuildEvents(buildContext, entry, changeCollector)
                .Where(auditEntry => !ShouldSuppressInstallationHierarchyChurn(auditEntry, reportsWithWorkKindChange))
                .Where(auditEntry => ShouldCaptureAuditEntry(buildContext, auditEntry)));
        }

        return auditEntries;
    }

    private static HashSet<Guid> ReportsWithWorkKindChange(DbContext dbContext) =>
        dbContext.ChangeTracker.Entries<JobReportRow>()
            .Where(entry => entry.State == EntityState.Modified
                && entry.Property(report => report.WorkKindId).IsModified)
            .Select(entry => entry.Entity.Id)
            .ToHashSet();

    private static bool ShouldSuppressInstallationHierarchyChurn(AuditEntry auditEntry, HashSet<Guid> reportsWithWorkKindChange) =>
        auditEntry.ReportId is Guid reportId
        && reportsWithWorkKindChange.Contains(reportId)
        && (auditEntry.Entry.Entity is JobReportInstallationRow
            or JobReportInstallationCategoryRow
            or JobReportInstallationControlPointRow);

    private static bool ShouldCaptureAuditEntry(AuditBuildContext context, AuditEntry auditEntry)
    {
        if (auditEntry.ReportId is not Guid reportId || reportId == Guid.Empty)
            return false;

        var reportState = ResolveReportState(context, auditEntry, reportId);
        if (reportState is null)
            return false;

        auditEntry.OrganizationId = reportState.Value.OrganizationId;

        if (IsTransitionToInReview(auditEntry))
            return true;

        var historyKey = (auditEntry.OrganizationId, reportId);
        if (!context.ReportHistoryExistsCache.TryGetValue(historyKey, out var hasExistingHistory))
        {
            hasExistingHistory = context.DbContext.Set<JobEventRow>().Local.Any(e => e.ReportId == reportId && e.OrganizationId == auditEntry.OrganizationId)
                || context.DbContext.Set<JobEventRow>().AsNoTracking().Any(e => e.ReportId == reportId && e.OrganizationId == auditEntry.OrganizationId);
            context.ReportHistoryExistsCache[historyKey] = hasExistingHistory;
        }

        if (hasExistingHistory)
            return true;

        return IsHistoryStatus(reportState.Value.CurrentStatus) || IsHistoryStatus(reportState.Value.OriginalStatus);
    }

    private static (Guid OrganizationId, string CurrentStatus, string OriginalStatus)? ResolveReportState(AuditBuildContext context, AuditEntry auditEntry, Guid reportId)
    {
        if (context.ReportStateCache.TryGetValue(reportId, out var cached))
            return cached;

        var dbContext = context.DbContext;
        var local = dbContext.Set<JobReportRow>().Local
            .FirstOrDefault(r => r.Id == reportId
                && (auditEntry.OrganizationId == Guid.Empty || r.OrganizationId == auditEntry.OrganizationId));
        if (local is not null)
        {
            var entry = dbContext.Entry(local);
            var originalStatus = entry.State is EntityState.Modified
                ? entry.Property(r => r.Status).OriginalValue
                : local.Status;
            var resolved = (local.OrganizationId, local.Status, originalStatus);
            context.ReportStateCache[reportId] = resolved;
            return resolved;
        }

        var stored = dbContext.Set<JobReportRow>().AsNoTracking()
            .Where(r => r.Id == reportId
                && (auditEntry.OrganizationId == Guid.Empty || r.OrganizationId == auditEntry.OrganizationId))
            .Select(r => new { r.OrganizationId, r.Status })
            .FirstOrDefault();
        (Guid OrganizationId, string CurrentStatus, string OriginalStatus)? storedState = stored is null
            ? null
            : (stored.OrganizationId, stored.Status, stored.Status);
        context.ReportStateCache[reportId] = storedState;
        return storedState;
    }

    private static bool IsTransitionToInReview(AuditEntry auditEntry) =>
        auditEntry.EventType == AuditEventTypes.Modified
        && auditEntry.AfterValues.TryGetValue(AuditSuffixes.Status, out var status)
        && string.Equals(status?.ToString(), JobStatus.InReview.ToString(), StringComparison.Ordinal);

    private static bool IsHistoryStatus(string status) =>
        !string.Equals(status, JobStatus.Draft.ToString(), StringComparison.Ordinal);

}
