using Microsoft.EntityFrameworkCore;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Infrastructure;

public sealed class JobStatusTransitionInterceptorTests
{
    [Fact]
    public async Task SaveChangesAsync_allows_valid_transition()
    {
        await using var context = CreateContext();
        var report = CreateReport(JobStatus.Draft);
        context.JobReports.Add(report);
        await context.SaveChangesAsync();

        context.Entry(report).Property(row => row.Status).CurrentValue = JobStatus.InReview.ToString();

        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();
        var persisted = await context.JobReports.SingleAsync();
        Assert.Equal(JobStatus.InReview.ToString(), persisted.Status);
    }

    [Fact]
    public async Task SaveChangesAsync_rejects_invalid_transition_before_persistence()
    {
        await using var context = CreateContext();
        var report = CreateReport(JobStatus.Approved);
        context.JobReports.Add(report);
        await context.SaveChangesAsync();

        context.Entry(report).Property(row => row.Status).CurrentValue = JobStatus.InReview.ToString();

        var exception = await Assert.ThrowsAsync<InvalidJobStatusTransitionException>(
            () => context.SaveChangesAsync());

        Assert.Equal(JobStatus.Approved, exception.CurrentStatus);
        Assert.Equal(JobStatus.InReview, exception.TargetStatus);

        context.ChangeTracker.Clear();
        var persisted = await context.JobReports.SingleAsync();
        Assert.Equal(JobStatus.Approved.ToString(), persisted.Status);
    }

    [Fact]
    public async Task SaveChangesAsync_preserves_hourly_value_when_job_is_approved()
    {
        await using var context = CreateContext();
        var organizationId = Guid.NewGuid();
        var user = new UserDataRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            FilialId = Guid.NewGuid(),
            Email = "cost@example.test",
            DisplayName = "Cost User",
            EntraId = "cost-user",
            EntraEmail = "cost@example.test",
            Phone = string.Empty,
            Role = Roles.User,
            BillableHourlyRate = 725m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var report = CreateReport(JobStatus.InReview, organizationId);
        var worksheet = new WorksheetRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            JobId = report.Id,
            UserId = user.Id,
            WorkDate = new DateTime(2026, 8, 14),
            HoursWorked = 3.5m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        context.Users.Add(user);
        context.JobReports.Add(report);
        context.Worksheets.Add(worksheet);
        await context.SaveChangesAsync();

        context.Entry(report).Property(row => row.Status).CurrentValue = JobStatus.Approved.ToString();
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();
        Assert.Equal(725m, (await context.Worksheets.SingleAsync()).BillableHourlyRateSnapshot);

        var persistedUser = await context.Users.SingleAsync();
        persistedUser.BillableHourlyRate = 900m;
        await context.SaveChangesAsync();

        context.ChangeTracker.Clear();
        Assert.Equal(725m, (await context.Worksheets.SingleAsync()).BillableHourlyRateSnapshot);
    }

    private static SqlDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new JobStatusTransitionInterceptor(), new ApprovedJobRateInterceptor())
            .Options;

        return new SqlDbContext(options);
    }

    private static JobReportRow CreateReport(JobStatus status, Guid? organizationId = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new JobReportRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId ?? Guid.NewGuid(),
            ReportNumber = "1",
            Status = status.ToString(),
            JobType = JobType.KLS,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
