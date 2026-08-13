using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

public sealed class ApprovedJobRateInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is SqlDbContext dbContext && !dbContext.IsSeeding)
        {
            dbContext.ChangeTracker.DetectChanges();
            var jobs = dbContext.ChangeTracker.Entries<JobReportRow>()
                .Where(entry => entry.State == EntityState.Modified)
                .Where(entry => entry.Property(report => report.Status).IsModified)
                .Where(entry => entry.Property(report => report.Status).CurrentValue == JobStatus.Approved.ToString())
                .Where(entry => entry.Property(report => report.Status).OriginalValue != JobStatus.Approved.ToString())
                .Select(entry => (entry.Entity.Id, entry.Entity.OrganizationId))
                .Distinct()
                .ToArray();

            if (jobs.Length > 0)
                await WorksheetRateSnapshots.CaptureAsync(dbContext, jobs, cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
