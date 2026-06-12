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
                var eventRows = auditEntries.Select(ae => new JobEventRow
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = ae.OrganizationId,
                    ReportId = ae.ReportId,
                    ActorId = ae.ActorId,
                    EventType = ae.EventType,
                    Summary = ae.Summary,
                    BeforeJson = ae.BeforeValues.Count > 0 ? JsonSerializer.Serialize(ae.BeforeValues, JsonOptions) : null,
                    AfterJson = ae.AfterValues.Count > 0 ? JsonSerializer.Serialize(ae.AfterValues, JsonOptions) : null,
                    CreatedAt = DateTimeOffset.UtcNow
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
                .Where(auditEntry => ShouldCaptureAuditEntry(dbContext, auditEntry)));
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

    private static bool ShouldCaptureAuditEntry(DbContext dbContext, AuditEntry auditEntry)
    {
        if (auditEntry.ReportId == Guid.Empty)
            return false;

        var reportState = ResolveReportState(dbContext, auditEntry);
        if (reportState is null)
            return false;

        auditEntry.OrganizationId = reportState.Value.OrganizationId;

        if (IsTransitionToInReview(auditEntry))
            return true;

        if (dbContext.Set<JobEventRow>().Local.Any(e => e.ReportId == auditEntry.ReportId && e.OrganizationId == auditEntry.OrganizationId))
            return true;

        if (dbContext.Set<JobEventRow>().AsNoTracking().Any(e => e.ReportId == auditEntry.ReportId && e.OrganizationId == auditEntry.OrganizationId))
            return true;

        return IsHistoryStatus(reportState.Value.CurrentStatus) || IsHistoryStatus(reportState.Value.OriginalStatus);
    }

    private static (Guid OrganizationId, string CurrentStatus, string OriginalStatus)? ResolveReportState(DbContext dbContext, AuditEntry auditEntry)
    {
        var local = dbContext.Set<JobReportRow>().Local
            .FirstOrDefault(r => r.Id == auditEntry.ReportId
                && (auditEntry.OrganizationId == Guid.Empty || r.OrganizationId == auditEntry.OrganizationId));
        if (local is not null)
        {
            var entry = dbContext.Entry(local);
            var originalStatus = entry.State is EntityState.Modified
                ? entry.Property(r => r.Status).OriginalValue
                : local.Status;
            return (local.OrganizationId, local.Status, originalStatus);
        }

        var stored = dbContext.Set<JobReportRow>().AsNoTracking()
            .Where(r => r.Id == auditEntry.ReportId
                && (auditEntry.OrganizationId == Guid.Empty || r.OrganizationId == auditEntry.OrganizationId))
            .Select(r => new { r.OrganizationId, r.Status })
            .FirstOrDefault();
        return stored is null ? null : (stored.OrganizationId, stored.Status, stored.Status);
    }

    private static bool IsTransitionToInReview(AuditEntry auditEntry) =>
        auditEntry.EventType == AuditEventTypes.Modified
        && auditEntry.AfterValues.TryGetValue("Status", out var status)
        && string.Equals(status?.ToString(), JobStatus.InReview.ToString(), StringComparison.Ordinal);

    private static bool IsHistoryStatus(string status) =>
        !string.Equals(status, JobStatus.Draft.ToString(), StringComparison.Ordinal);

}
