using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

public sealed class JobStatusTransitionInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ValidateTransitions(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ValidateTransitions(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void ValidateTransitions(DbContext? dbContext)
    {
        if (dbContext is null)
        {
            return;
        }

        if (dbContext is SqlDbContext { IsSeeding: true })
        {
            return;
        }

        dbContext.ChangeTracker.DetectChanges();

        foreach (var entry in dbContext.ChangeTracker.Entries<JobReportRow>())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            var statusProperty = entry.Property(report => report.Status);
            if (!statusProperty.IsModified)
            {
                continue;
            }

            if (!Enum.TryParse<JobStatus>(statusProperty.OriginalValue, out var currentStatus)
                || !Enum.TryParse<JobStatus>(statusProperty.CurrentValue, out var targetStatus))
            {
                throw new InvalidOperationException("A job report contains an unsupported status value.");
            }

            if (!JobStatusTransitionPolicy.IsSourceTransitionAllowed(currentStatus, targetStatus))
            {
                throw new InvalidJobStatusTransitionException(currentStatus, targetStatus);
            }
        }
    }
}
