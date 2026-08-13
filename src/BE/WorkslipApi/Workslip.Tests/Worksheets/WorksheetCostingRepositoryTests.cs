using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Domain;
using Workslip.Domain.Models;
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
        await context.Database.EnsureCreatedAsync();
        await CreateBillingTablesAsync(context);

        var organizationId = Guid.NewGuid();
        var user = CreateUser(organizationId);
        var openJob = CreateJob(organizationId, "OPEN", JobStatus.InReview);
        var approvedJob = CreateJob(organizationId, "DONE", JobStatus.Approved);
        var openWorksheet = CreateWorksheet(organizationId, openJob.Id, user.Id, 2m);
        var approvedWorksheet = CreateWorksheet(organizationId, approvedJob.Id, user.Id, 3.5m);

        context.Users.Add(user);
        context.JobReports.AddRange(openJob, approvedJob);
        context.Worksheets.AddRange(openWorksheet, approvedWorksheet);
        await context.SaveChangesAsync();

        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO UserBillingRates (OrganizationId, UserId, BillableHourlyRate, UpdatedAt)
            VALUES ({organizationId}, {user.Id}, {900m}, {DateTimeOffset.UtcNow});
            """);
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO WorksheetBillingSnapshots (OrganizationId, WorksheetId, BillableHourlyRateSnapshot, CapturedAtUtc)
            VALUES ({organizationId}, {approvedWorksheet.Id}, {725m}, {DateTimeOffset.UtcNow});
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
        Assert.Equal(2537.50m, approved.BillableAmount);
    }

    private static Task CreateBillingTablesAsync(SqlDbContext context) =>
        context.Database.ExecuteSqlRawAsync("""
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

    private static UserDataRow CreateUser(Guid organizationId) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        FilialId = Guid.NewGuid(),
        Email = "costing@example.test",
        DisplayName = "Cost User",
        EntraId = "costing-user",
        EntraEmail = "costing@example.test",
        Phone = string.Empty,
        Role = Roles.User,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static JobReportRow CreateJob(Guid organizationId, string number, JobStatus status) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        ReportNumber = number,
        Status = status.ToString(),
        JobType = JobType.KLS,
        CustomerName = "Kunde",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static WorksheetRow CreateWorksheet(
        Guid organizationId,
        Guid jobId,
        Guid userId,
        decimal hours) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        JobId = jobId,
        UserId = userId,
        WorkDate = new DateTime(2026, 8, 14),
        HoursWorked = hours,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

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
