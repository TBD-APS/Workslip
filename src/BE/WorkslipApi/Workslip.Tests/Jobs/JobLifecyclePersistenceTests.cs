using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;

namespace Workslip.Tests.Jobs;

public sealed class JobLifecyclePersistenceTests
{
    [Fact]
    public async Task TransitionAsync_Reopened_PersistsCorrectionReasonWithStatus()
    {
        var organizationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        const string reason = "Kunden kræver en rettelse i dokumentationen.";

        await using var context = CreateContext(organizationId, actorId);
        context.IsSeeding = true;
        context.Organizations.Add(new OrganizationRow
        {
            Id = organizationId,
            Name = "TestOrg",
            Cvr = "12345678"
        });
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

        var repository = CreateJobRepository(context, actorId, organizationId);

        var transition = await repository.TransitionAsync(
            jobId,
            organizationId,
            JobStatus.Reopened,
            actorId,
            reason,
            CancellationToken.None);

        Assert.NotNull(transition);
        Assert.True(transition.Changed);
        Assert.Equal(JobStatus.Reopened, transition.Report.Status);
        Assert.Equal(reason, transition.Report.RejectionNote);

        var persisted = await context.JobReports
            .AsNoTracking()
            .SingleAsync(report => report.Id == jobId);
        Assert.Equal(JobStatus.Reopened.ToString(), persisted.Status);
        Assert.Equal(reason, persisted.RejectionNote);
    }

    private static EfJobRepository CreateJobRepository(
        SqlDbContext context,
        Guid userId,
        Guid organizationId)
    {
        var retryPolicy = new NoRetryPolicy();
        var currentUser = new TestCurrentUserContext(userId, organizationId);
        var worksheetRepository = new EfWorksheetRepository(context, currentUser, retryPolicy);
        var jobViewRepository = new EfJobViewRepository(NullLogger<EfJobViewRepository>.Instance, context);
        var assignmentRepository = new EfAssignmentRepository(
            context,
            retryPolicy,
            currentUser,
            worksheetRepository,
            jobViewRepository);
        var linkRepository = new EfJobLinkRepository(context, retryPolicy);

        return new EfJobRepository(
            context,
            retryPolicy,
            new EfCustomerRepository(context, retryPolicy),
            assignmentRepository,
            linkRepository,
            worksheetRepository,
            jobViewRepository);
    }

    private static SqlDbContext CreateContext(Guid organizationId, Guid userId)
    {
        var currentUser = new TestCurrentUserContext(userId, organizationId);
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .AddInterceptors(
                new JobStatusTransitionInterceptor(),
                new AuditInterceptor(currentUser))
            .Options;

        return new SqlDbContext(options);
    }

    private sealed class TestCurrentUserContext(Guid userId, Guid organizationId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
        public Guid? OrganizationId { get; } = organizationId;
        public string? Role => Roles.Admin;
    }

    private sealed class NoRetryPolicy : IDatabaseRetryPolicy
    {
        public Task ExecuteAsync(
            string operationName,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken) =>
            operation(cancellationToken);

        public Task<T> ExecuteAsync<T>(
            string operationName,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken) =>
            operation(cancellationToken);
    }
}
