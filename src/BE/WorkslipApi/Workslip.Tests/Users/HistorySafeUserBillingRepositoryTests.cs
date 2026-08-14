using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Users;

public sealed class HistorySafeUserBillingRepositoryTests
{
    [Fact]
    public async Task Rate_change_preserves_previous_rate_for_approved_worksheet_without_snapshot()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new SqlDbContext(options);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE UserBillingRates (
                OrganizationId TEXT NOT NULL,
                UserId TEXT NOT NULL,
                BillableHourlyRate NUMERIC NULL,
                UpdatedAt TEXT NOT NULL,
                PRIMARY KEY (OrganizationId, UserId)
            );
            CREATE TABLE WorksheetBillingSnapshots (
                OrganizationId TEXT NOT NULL,
                WorksheetId TEXT NOT NULL,
                BillableHourlyRateSnapshot NUMERIC NULL,
                CapturedAtUtc TEXT NOT NULL,
                PRIMARY KEY (OrganizationId, WorksheetId)
            );
            CREATE TABLE JobReports (
                Id TEXT NOT NULL PRIMARY KEY,
                OrganizationId TEXT NOT NULL,
                Status TEXT NOT NULL
            );
            CREATE TABLE Worksheets (
                Id TEXT NOT NULL PRIMARY KEY,
                OrganizationId TEXT NOT NULL,
                JobId TEXT NOT NULL,
                UserId TEXT NOT NULL
            );
            """);

        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var worksheetId = Guid.NewGuid();

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO UserBillingRates (OrganizationId, UserId, BillableHourlyRate, UpdatedAt)
            VALUES ({organizationId}, {userId}, {725m}, {DateTimeOffset.UtcNow});
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO JobReports (Id, OrganizationId, Status)
            VALUES ({jobId}, {organizationId}, {"Approved"});
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO Worksheets (Id, OrganizationId, JobId, UserId)
            VALUES ({worksheetId}, {organizationId}, {jobId}, {userId});
            """);

        var inner = new SqlUserBillingRepository(context);
        var repository = new HistorySafeUserBillingRepository(inner, context);

        await repository.SetRateAsync(organizationId, userId, 900m, CancellationToken.None);

        Assert.Equal(900m, await repository.GetRateAsync(organizationId, userId, CancellationToken.None));
        var preserved = await context.Database.SqlQueryRaw<decimal?>(
            "SELECT BillableHourlyRateSnapshot AS Value FROM WorksheetBillingSnapshots LIMIT 1")
            .SingleAsync();
        Assert.Equal(725m, preserved);
    }
}
