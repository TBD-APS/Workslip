using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

/// <summary>
/// Persistence-level safety net: once a job is approved, the job and every
/// EF-tracked IJobRelated entity are immutable until the explicit
/// Approved -> Reopened transition has been committed.
/// </summary>
public sealed class ApprovedJobImmutabilityGuard : SaveChangesInterceptor
{
    public const string LockedMessage = "Den godkendte sag er låst. Genåbn sagen med en begrundelse før du ændrer den.";

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is SqlDbContext context)
            ValidateAsync(context, CancellationToken.None).GetAwaiter().GetResult();

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is SqlDbContext context)
            await ValidateAsync(context, cancellationToken);

        return result;
    }

    private static async Task ValidateAsync(
        SqlDbContext context,
        CancellationToken cancellationToken)
    {
        if (context.IsSeeding)
            return;

        context.ChangeTracker.DetectChanges();

        var changedEntries = context.ChangeTracker.Entries()
            .Where(entry => entry.Entity is IJobRelated
                && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToArray();

        if (changedEntries.Length == 0)
            return;

        var jobIdsToCheck = new HashSet<Guid>();

        foreach (var entry in changedEntries)
        {
            if (entry.Entity is JobReportRow report)
            {
                if (entry.State == EntityState.Added)
                    continue;

                var originalStatus = entry.State == EntityState.Modified
                    ? entry.Property(nameof(JobReportRow.Status)).OriginalValue?.ToString()
                    : report.Status;

                if (!string.Equals(originalStatus, JobStatus.Approved.ToString(), StringComparison.Ordinal))
                    continue;

                if (IsControlledReopen(entry))
                    continue;

                throw new ApprovedJobImmutableException(report.Id);
            }

            var related = (IJobRelated)entry.Entity;
            if (related.JobReportId != Guid.Empty)
                jobIdsToCheck.Add(related.JobReportId);
        }

        if (jobIdsToCheck.Count == 0)
            return;

        var approvedJobId = await context.JobReports
            .AsNoTracking()
            .Where(report => jobIdsToCheck.Contains(report.Id)
                && report.Status == JobStatus.Approved.ToString())
            .Select(report => (Guid?)report.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (approvedJobId is Guid id)
            throw new ApprovedJobImmutableException(id);
    }

    private static bool IsControlledReopen(EntityEntry entry)
    {
        if (entry.State != EntityState.Modified)
            return false;

        var status = entry.Property(nameof(JobReportRow.Status));
        if (!status.IsModified
            || !string.Equals(status.OriginalValue?.ToString(), JobStatus.Approved.ToString(), StringComparison.Ordinal)
            || !string.Equals(status.CurrentValue?.ToString(), JobStatus.Reopened.ToString(), StringComparison.Ordinal))
        {
            return false;
        }

        var allowedProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(JobReportRow.Status),
            nameof(JobReportRow.UpdatedAt),
            nameof(JobReportRow.RejectionNote)
        };

        return entry.Properties
            .Where(property => property.IsModified)
            .All(property => allowedProperties.Contains(property.Metadata.Name));
    }
}

public sealed class ApprovedJobImmutableException(Guid jobId)
    : InvalidOperationException(ApprovedJobImmutabilityGuard.LockedMessage)
{
    public Guid JobId { get; } = jobId;
}
