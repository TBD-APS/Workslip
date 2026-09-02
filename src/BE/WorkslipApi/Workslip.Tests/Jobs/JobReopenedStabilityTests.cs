using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Jobs;

/// <summary>
/// Stability guard for the Approved -> Reopened lifecycle.
/// Covers the EF interceptor chain that protects approved data.
/// The database CHECK constraint itself is asserted in
/// SchemaModelContractTests.JobReports_status_check_constraint_allows_reopened
/// (InMemory does not enforce CHECK, so this fixture focuses on the
/// application-level guards and the full transition lifecycle).
/// </summary>
public sealed class JobReopenedStabilityTests
{
    [Fact]
    public async Task Reopen_persists_reason_and_allows_worksheet_mutation_after_reopen()
    {
        var organizationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        const string reason = "Dokumentationen skal rettes efter godkendelse.";

        var reopenContext = new JobReopenReasonContext();
        await using var context = CreateContext(reopenContext, organizationId, actorId);

        // Seed Approved job directly (bypass guards via IsSeeding)
        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = organizationId, Name = "TestOrg", Cvr = "12345678" });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = organizationId,
            ReportNumber = "0042",
            Status = JobStatus.Approved.ToString(),
            JobType = JobType.KLS,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        var efRepository = CreateEfRepository(context, organizationId, actorId);

        // 1) Reopen must succeed and persist reason atomically – mimics BillingAwareJobRepository
        JobTransitionResult? transition;
        using (reopenContext.Begin(jobId, organizationId, reason))
        {
            transition = await efRepository.TransitionAsync(jobId, organizationId, JobStatus.Reopened, actorId, reason, CancellationToken.None);
        }
        Assert.NotNull(transition);
        Assert.True(transition.Changed);
        Assert.Equal(JobStatus.Reopened, transition.Report.Status);
        Assert.Equal(reason, transition.Report.RejectionNote);

        var persisted = await context.JobReports.AsNoTracking().SingleAsync(r => r.Id == jobId);
        Assert.Equal(JobStatus.Reopened.ToString(), persisted.Status);
        Assert.Equal(reason, persisted.RejectionNote);

        // 2) After reopen the finalized guards must no longer block worksheet mutations
        context.Worksheets.Add(new WorksheetRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            JobId = jobId,
            UserId = actorId,
            WorkDate = DateTime.UtcNow.Date,
            HoursWorked = 2.5m,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        // Should not throw WorksheetFinalizationGuard / ApprovedJobImmutabilityGuard
        await context.SaveChangesAsync();

        var worksheetCount = await context.Worksheets.CountAsync(w => w.JobId == jobId);
        Assert.Equal(1, worksheetCount);

        // 3) Reopened -> InReview (resubmit) must be allowed and clears the correction reason
        var resubmit = await efRepository.TransitionAsync(jobId, organizationId, JobStatus.InReview, actorId, null, CancellationToken.None);
        Assert.NotNull(resubmit);
        Assert.True(resubmit.Changed);
        Assert.Equal(JobStatus.InReview, resubmit.Report.Status);
        Assert.Null(resubmit.Report.RejectionNote);

        var afterSubmit = await context.JobReports.AsNoTracking().SingleAsync(r => r.Id == jobId);
        Assert.Equal(JobStatus.InReview.ToString(), afterSubmit.Status);
        Assert.Null(afterSubmit.RejectionNote);
    }

    [Fact]
    public async Task Reopen_without_reason_is_rejected_by_guard()
    {
        var organizationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var reopenContext = new JobReopenReasonContext();
        await using var context = CreateContext(reopenContext, organizationId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = organizationId, Name = "TestOrg2", Cvr = "87654321" });
        context.JobReports.Add(new JobReportRow
        {
            Id = jobId,
            OrganizationId = organizationId,
            ReportNumber = "0043",
            Status = JobStatus.Approved.ToString(),
            JobType = JobType.KLS,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        var efRepository = CreateEfRepository(context, organizationId, actorId);

        // Guard is the safety net – BillingAware would not have started a scope for whitespace/null
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => efRepository.TransitionAsync(jobId, organizationId, JobStatus.Reopened, actorId, "   ", CancellationToken.None));

        Assert.Equal(ApprovedJobImmutabilityGuard.MissingReopenReasonMessage, exception.Message);

        // Also null reason must be rejected
        var nullException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => efRepository.TransitionAsync(jobId, organizationId, JobStatus.Reopened, actorId, null, CancellationToken.None));
        Assert.Equal(ApprovedJobImmutabilityGuard.MissingReopenReasonMessage, nullException.Message);
    }

    [Fact]
    public async Task Approved_job_direct_mutation_without_reopen_is_blocked()
    {
        var organizationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var reopenContext = new JobReopenReasonContext();
        await using var context = CreateContext(reopenContext, organizationId, actorId);

        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow { Id = organizationId, Name = "TestOrg3", Cvr = "11223344" });
        var job = new JobReportRow
        {
            Id = jobId,
            OrganizationId = organizationId,
            ReportNumber = "0044",
            Status = JobStatus.Approved.ToString(),
            JobType = JobType.KLS,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        context.JobReports.Add(job);
        await context.SaveChangesAsync();
        context.IsSeeding = false;

        context.Entry(job).Property(r => r.DestinationAddress).CurrentValue = "Ny adresse";

        var exception = await Assert.ThrowsAsync<ApprovedJobImmutableException>(() => context.SaveChangesAsync());
        Assert.Equal(jobId, exception.JobId);
    }

    private static SqlDbContext CreateContext(JobReopenReasonContext reopenContext, Guid organizationId, Guid userId)
    {
        var currentUser = new TestCurrentUserContext(userId, organizationId);
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(
                new JobStatusTransitionInterceptor(),
                new ApprovedJobImmutabilityGuard(reopenContext),
                new AuditInterceptor(currentUser),
                new WorksheetFinalizationGuard())
            .Options;

        return new SqlDbContext(options);
    }

    private static EfJobRepository CreateEfRepository(SqlDbContext context, Guid organizationId, Guid actorId)
    {
        var retryPolicy = new NoRetryPolicy();
        var currentUser = new TestCurrentUserContext(actorId, organizationId);
        var worksheetRepo = new EfWorksheetRepository(context, currentUser, retryPolicy);
        var jobViewRepo = new EfJobViewRepository(NullLogger<EfJobViewRepository>.Instance, context);
        var assignmentRepo = new EfAssignmentRepository(context, retryPolicy, currentUser, worksheetRepo, jobViewRepo);
        var linkRepo = new EfJobLinkRepository(context, retryPolicy);
        return new EfJobRepository(
            context,
            retryPolicy,
            new EfCustomerRepository(context, retryPolicy),
            assignmentRepo,
            linkRepo,
            worksheetRepo,
            jobViewRepo);
    }

    private sealed class TestCurrentUserContext(Guid userId, Guid organizationId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
        public Guid? OrganizationId { get; } = organizationId;
        public string? Role => Roles.Admin;
    }

    private sealed class NoRetryPolicy : IDatabaseRetryPolicy
    {
        public Task ExecuteAsync(string operationName, Func<CancellationToken, Task> operation, CancellationToken cancellationToken) => operation(cancellationToken);
        public Task<T> ExecuteAsync<T>(string operationName, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) => operation(cancellationToken);
    }
}


