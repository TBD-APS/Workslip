using System.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Workslip.Infrastructure.Schema;

internal static class WorksheetBillingSnapshots
{
    internal static async Task CaptureAsync(
        SqlDbContext dbContext,
        IReadOnlyList<(Guid JobId, Guid OrganizationId)> jobs,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            var transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            var rateTable = TableName(dbContext, "UserBillingRates");
            var snapshotTable = TableName(dbContext, "WorksheetBillingSnapshots");

            foreach (var job in jobs)
            {
                var worksheets = await dbContext.Worksheets
                    .AsNoTracking()
                    .Where(row => row.OrganizationId == job.OrganizationId && row.JobId == job.JobId)
                    .Select(row => new { row.Id, row.UserId })
                    .ToListAsync(cancellationToken);

                if (worksheets.Count == 0)
                    continue;

                var userIds = worksheets.Select(row => row.UserId).Distinct().ToArray();
                var rateCommand = new CommandDefinition(
                    $"SELECT UserId, BillableHourlyRate FROM {rateTable} WHERE OrganizationId = @OrganizationId AND UserId IN @UserIds",
                    new { OrganizationId = job.OrganizationId, UserIds = userIds },
                    transaction,
                    cancellationToken: cancellationToken);

                var rates = (await connection.QueryAsync<RateRow>(rateCommand))
                    .ToDictionary(row => row.UserId, row => row.BillableHourlyRate);

                foreach (var worksheet in worksheets)
                {
                    rates.TryGetValue(worksheet.UserId, out var rate);
                    var snapshotCommand = new CommandDefinition(
                        $"""
                         INSERT INTO {snapshotTable} (OrganizationId, WorksheetId, BillableHourlyRateSnapshot, CapturedAtUtc)
                         SELECT @OrganizationId, @WorksheetId, @Rate, @CapturedAtUtc
                         WHERE NOT EXISTS (
                             SELECT 1 FROM {snapshotTable}
                             WHERE OrganizationId = @OrganizationId AND WorksheetId = @WorksheetId
                         );
                         """,
                        new
                        {
                            OrganizationId = job.OrganizationId,
                            WorksheetId = worksheet.Id,
                            Rate = rate,
                            CapturedAtUtc = DateTimeOffset.UtcNow
                        },
                        transaction,
                        cancellationToken: cancellationToken);

                    await connection.ExecuteAsync(snapshotCommand);
                }
            }
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private static string TableName(SqlDbContext dbContext, string name) =>
        string.Equals(
            dbContext.Database.ProviderName,
            "Microsoft.EntityFrameworkCore.SqlServer",
            StringComparison.Ordinal)
            ? $"dbo.{name}"
            : name;

    private sealed record RateRow(Guid UserId, decimal? BillableHourlyRate);
}
