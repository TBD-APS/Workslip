using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

public sealed class ApprovedWorksheetMutationInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is SqlDbContext dbContext && !dbContext.IsSeeding)
            ThrowIfApprovedWorksheetIsChanging(dbContext);

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is SqlDbContext dbContext && !dbContext.IsSeeding)
            await ThrowIfApprovedWorksheetIsChangingAsync(dbContext, cancellationToken);

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void ThrowIfApprovedWorksheetIsChanging(SqlDbContext dbContext)
    {
        var scopes = GetChangedWorksheetScopes(dbContext);
        foreach (var scope in scopes)
        {
            if (dbContext.JobReports.AsNoTracking().Any(report =>
                    report.Id == scope.JobId
                    && report.OrganizationId == scope.OrganizationId
                    && report.Status == nameof(JobStatus.Approved)))
            {
                throw new InvalidOperationException("Worksheets on an approved job are immutable.");
            }
        }
    }

    private static async Task ThrowIfApprovedWorksheetIsChangingAsync(
        SqlDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var scopes = GetChangedWorksheetScopes(dbContext);
        foreach (var scope in scopes)
        {
            if (await dbContext.JobReports.AsNoTracking().AnyAsync(report =>
                    report.Id == scope.JobId
                    && report.OrganizationId == scope.OrganizationId
                    && report.Status == nameof(JobStatus.Approved),
                cancellationToken))
            {
                throw new InvalidOperationException("Worksheets on an approved job are immutable.");
            }
        }
    }

    private static IReadOnlyList<(Guid JobId, Guid OrganizationId)> GetChangedWorksheetScopes(SqlDbContext dbContext)
    {
        dbContext.ChangeTracker.DetectChanges();
        return dbContext.ChangeTracker.Entries<WorksheetRow>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Where(entry => entry.State != EntityState.Modified
                || entry.Properties.Any(property => property.IsModified
                    && property.Metadata.Name != nameof(WorksheetRow.BillableHourlyRateSnapshot)))
            .Select(entry => (entry.Entity.JobId, entry.Entity.OrganizationId))
            .Distinct()
            .ToArray();
    }
}
