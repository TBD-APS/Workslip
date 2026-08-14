using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Domain;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Worksheets;

public sealed class WorksheetCostingRepositoryTests
{
    [Fact]
    public async Task Admin_month_uses_current_rate_for_open_work_and_snapshot_for_approved_work()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new SqlDbContext(options);
        await CreateSchemaAsync(context);

        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var openJobId = Guid.NewGuid();
        var approvedJobId = Guid.NewGuid();
        var openWorksheetId = Guid.NewGuid();
        var approvedWorksheetId = Guid.NewGuid();
        var workDate = new DateTime(2026, 8, 14);

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO Users (Id, OrganizationId, DisplayName)
            VALUES ({userId}, {organizationId}, {"Cost User"});
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO JobReports (Id, OrganizationId, ReportNumber, Status, CustomerName, IsSoftDeleted, JobType)
            VALUES ({openJobId}, {organizationId}, {"OPEN"}, {JobStatus.InReview.ToString()}, {"Kunde"}, {false}, {(int)JobType.KLS});
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO JobReports (Id, OrganizationId, ReportNumber, Status, CustomerName, IsSoftDeleted, JobType)
            VALUES ({approvedJobId}, {organizationId}, {"DONE"}, {JobStatus.Approved.ToString()}, {"Kunde"}, {false}, {(int)JobType.KLS});
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO Worksheets (Id, OrganizationId, JobId, UserId, WorkDate, HoursWorked, SleptOnJob)
            VALUES ({openWorksheetId}, {organizationId}, {openJobId}, {userId}, {workDate}, {2m}, {false});
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO Worksheets (Id, OrganizationId, JobId, UserId, WorkDate, HoursWorked, SleptOnJob)
            VALUES ({approvedWorksheetId}, {organizationId}, {approvedJobId}, {userId}, {workDate}, {3.501m}, {false});
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO UserBillingRates (OrganizationId, UserId, BillableHourlyRate, UpdatedAt)
            VALUES ({organizationId}, {userId}, {900m}, {DateTimeOffset.UtcNow});
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO WorksheetBillingSnapshots (OrganizationId, WorksheetId, BillableHourlyRateSnapshot, CapturedAtUtc)
            VALUES ({organizationId}, {approvedWorksheetId}, {725m}, {DateTimeOffset.UtcNow});
            """);

        var repository = new EfWorksheetRepository(
            context,
            new TestCurrentUserContext(Guid.NewGuid(), organizationId),
            new NoRetryPolicy());

        var rows = await repository.GetAllWorksheetsAsync(
            organizationId,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            CancellationToken.None);

        var open = Assert.Single(rows, row => row.ReportNumber == "OPEN");
        Assert.Equal(900m, open.BillableHourlyRate);
        Assert.Equal(1800m, open.BillableAmount);

        var approved = Assert.Single(rows, row => row.ReportNumber == "DONE");
        Assert.Equal(725m, approved.BillableHourlyRate);
        Assert.Equal(2538.23m, approved.BillableAmount);
    }

    private static Task CreateSchemaAsync(SqlDbContext context) =>
        context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE Users (
                Id TEXT NOT NULL PRIMARY KEY,
                OrganizationId TEXT NOT NULL,
                DisplayName TEXT NOT NULL
            );
            CREATE TABLE Customers (
                Id TEXT NOT NULL PRIMARY KEY,
                OrganizationId TEXT NOT NULL,
                Name TEXT NOT NULL,
                Address TEXT NULL
            );
            CREATE TABLE JobReports (
                Id TEXT NOT NULL PRIMARY KEY,
                OrganizationId TEXT NOT NULL,
                CustomerId TEXT NULL,
                ReportNumber TEXT NULL,
                Status TEXT NOT NULL,
                CustomerName TEXT NULL,
                CustomerAddress TEXT NULL,
                IsSoftDeleted INTEGER NOT NULL,
                JobType INTEGER NOT NULL
            );
            CREATE TABLE Worksheets (
                Id TEXT NOT NULL PRIMARY KEY,
                OrganizationId TEXT NOT NULL,
                JobId TEXT NOT NULL,
                UserId TEXT NOT NULL,
                WorkDate TEXT NOT NULL,
                HoursWorked NUMERIC NOT NULL,
                SleptOnJob INTEGER NOT NULL
            );
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
            """);

    private sealed class TestCurrentUserContext(Guid userId, Guid organizationId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
        public Guid? OrganizationId { get; } = organizationId;
        public string? Role => Roles.Admin;
    }

    private sealed class NoRetryPolicy : IDatabaseRetryPolicy
    {
        public Task ExecuteAsync(string operationName, Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);

        public Task<T> ExecuteAsync<T>(string operationName, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
            operation(cancellationToken);
    }
}
