using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

public sealed class WorksheetFinalizationGuard : SaveChangesInterceptor
{
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
        var entries = context.ChangeTracker
            .Entries<WorksheetRow>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToArray();

        if (entries.Length == 0)
            return;

        var jobKeys = GetJobKeys(entries);
        foreach (var key in jobKeys)
        {
            var finalized = await context.JobReports
                .AsNoTracking()
                .AnyAsync(report =>
                    report.OrganizationId == key.OrganizationId
                    && report.Id == key.JobId
                    && report.Status == JobStatus.Approved.ToString(),
                    cancellationToken);

            if (finalized)
                throw new InvalidOperationException("Finalized worksheet history is immutable.");
        }
    }

    private static IReadOnlySet<JobKey> GetJobKeys(
        IReadOnlyList<EntityEntry<WorksheetRow>> entries)
    {
        var keys = new HashSet<JobKey>();
        foreach (var entry in entries)
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
                keys.Add(new JobKey(entry.Entity.OrganizationId, entry.Entity.JobId));

            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                keys.Add(new JobKey(
                    entry.OriginalValues.GetValue<Guid>(nameof(WorksheetRow.OrganizationId)),
                    entry.OriginalValues.GetValue<Guid>(nameof(WorksheetRow.JobId))));
            }
        }

        return keys;
    }

    private readonly record struct JobKey(Guid OrganizationId, Guid JobId);
}
