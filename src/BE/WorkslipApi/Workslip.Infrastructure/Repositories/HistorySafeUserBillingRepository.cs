using System.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Workslip.Application.Users;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

internal sealed class HistorySafeUserBillingRepository(
    SqlUserBillingRepository inner,
    SqlDbContext dbContext) : IUserBillingRepository
{
    public Task<decimal?> GetRateAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken) =>
        inner.GetRateAsync(organizationId, userId, cancellationToken);

    public async Task SetRateAsync(
        Guid organizationId,
        Guid userId,
        decimal? rate,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var previousRate = await inner.GetRateAsync(organizationId, userId, cancellationToken);
        await PreserveApprovedHistoryAsync(
            organizationId,
            userId,
            previousRate,
            cancellationToken);

        await inner.SetRateAsync(organizationId, userId, rate, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task PreserveApprovedHistoryAsync(
        Guid organizationId,
        Guid userId,
        decimal? previousRate,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
        var snapshots = WorksheetBillingSnapshots.TableName(dbContext, "WorksheetBillingSnapshots");
        var worksheets = WorksheetBillingSnapshots.TableName(dbContext, "Worksheets");
        var jobs = WorksheetBillingSnapshots.TableName(dbContext, "JobReports");

        var command = new CommandDefinition(
            $"""
             INSERT INTO {snapshots} (OrganizationId, WorksheetId, BillableHourlyRateSnapshot, CapturedAtUtc)
             SELECT @OrganizationId, worksheet.Id, @PreviousRate, @CapturedAtUtc
             FROM {worksheets} AS worksheet
             INNER JOIN {jobs} AS job
                 ON job.Id = worksheet.JobId
                 AND job.OrganizationId = worksheet.OrganizationId
             WHERE worksheet.OrganizationId = @OrganizationId
               AND worksheet.UserId = @UserId
               AND job.Status = 'Approved'
               AND NOT EXISTS (
                   SELECT 1
                   FROM {snapshots} AS existing
                   WHERE existing.OrganizationId = worksheet.OrganizationId
                     AND existing.WorksheetId = worksheet.Id
               );
             """,
            new
            {
                OrganizationId = organizationId,
                UserId = userId,
                PreviousRate = previousRate,
                CapturedAtUtc = DateTimeOffset.UtcNow
            },
            transaction,
            cancellationToken: cancellationToken);

        await connection.ExecuteAsync(command);
    }
}
