using Microsoft.EntityFrameworkCore;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Jobs;

public sealed class ApprovedJobImmutabilityGuardTests
{
    [Fact]
    public async Task Approved_job_rejects_direct_mutation()
    {
        var reasonContext = new JobReopenReasonContext();
        await using var context = CreateContext(reasonContext);
        var job = await AddApprovedJobAsync(context);

        context.Entry(job).Property(report => report.DestinationAddress).CurrentValue = "Ny adresse";

        var exception = await Assert.ThrowsAsync<ApprovedJobImmutableException>(
            () => context.SaveChangesAsync());

        Assert.Equal(job.Id, exception.JobId);
        Assert.Equal(ApprovedJobImmutabilityGuard.LockedMessage, exception.Message);
    }

    [Fact]
    public async Task Approved_job_rejects_related_entity_mutation()
    {
        var reasonContext = new JobReopenReasonContext();
        await using var context = CreateContext(reasonContext);
        var job = await AddApprovedJobAsync(context);

        context.JobAssignments.Add(new JobAssignmentRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = job.OrganizationId,
            ReportId = job.Id,
            UserId = Guid.NewGuid(),
            AssignedAt = DateTimeOffset.UtcNow
        });

        var exception = await Assert.ThrowsAsync<ApprovedJobImmutableException>(
            () => context.SaveChangesAsync());

        Assert.Equal(job.Id, exception.JobId);
    }

    [Fact]
    public async Task Approved_job_rejects_reopen_without_reason_context()
    {
        var reasonContext = new JobReopenReasonContext();
        await using var context = CreateContext(reasonContext);
        var job = await AddApprovedJobAsync(context);

        context.Entry(job).Property(report => report.Status).CurrentValue = JobStatus.Reopened.ToString();
        context.Entry(job).Property(report => report.UpdatedAt).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(1);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.SaveChangesAsync());

        Assert.Equal(ApprovedJobImmutabilityGuard.MissingReopenReasonMessage, exception.Message);
    }

    [Fact]
    public async Task Approved_job_allows_controlled_reopen_and_persists_reason_in_same_save()
    {
        var reasonContext = new JobReopenReasonContext();
        await using var context = CreateContext(reasonContext);
        var job = await AddApprovedJobAsync(context);
        var entry = context.Entry(job);

        entry.Property(report => report.Status).CurrentValue = JobStatus.Reopened.ToString();
        entry.Property(report => report.UpdatedAt).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(1);

        using (reasonContext.Begin(job.Id, job.OrganizationId, "Dokumentationen skal rettes"))
        {
            await context.SaveChangesAsync();
        }

        Assert.Equal(JobStatus.Reopened.ToString(), job.Status);
        Assert.Equal("Dokumentationen skal rettes", job.RejectionNote);
    }

    private static SqlDbContext CreateContext(JobReopenReasonContext reasonContext)
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new ApprovedJobImmutabilityGuard(reasonContext))
            .Options;

        return new SqlDbContext(options);
    }

    private static async Task<JobReportRow> AddApprovedJobAsync(SqlDbContext context)
    {
        var job = new JobReportRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            FilialId = Guid.NewGuid(),
            Status = JobStatus.Approved.ToString(),
            ReportNumber = "LOCKED",
            JobType = JobType.KLS,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        context.JobReports.Add(job);
        await context.SaveChangesAsync();
        return job;
    }
}
