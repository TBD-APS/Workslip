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

    private static SqlDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new JobStatusTransitionInterceptor())
            .Options;

        return new SqlDbContext(options);
    }

    private static JobReportRow CreateReport(JobStatus status)
    {
        var now = DateTimeOffset.UtcNow;
        return new JobReportRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            ReportNumber = "1",
            Status = status.ToString(),
            JobType = JobType.KLS,
            CreatedAt = now,
            UpdatedAt = now
        };
    }
}
