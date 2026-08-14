using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Worksheets;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Jobs;

public sealed class AuthorizedJobServiceTests
{
    [Fact]
    public async Task ChangeStatusAsync_forbids_user_approval_before_inner_service_runs()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repository = new StubJobRepository(CreateJob(organizationId, JobStatus.InReview) with
        {
            AssignedUsers = [new AssignedUserResponse(userId, "Current employee")]
        });
        var service = CreateService(repository, organizationId, Roles.User, userId);

        var result = await service.ChangeStatusAsync(
            repository.Job!.Id,
            new ChangeJobStatusRequest(JobStatus.Approved),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Equal(1, repository.GetSingleCalls);
        Assert.Equal(0, repository.TransitionCalls);
    }

    [Fact]
    public async Task ChangeStatusAsync_returns_conflict_for_invalid_admin_transition()
    {
        var organizationId = Guid.NewGuid();
        var repository = new StubJobRepository(CreateJob(organizationId, JobStatus.Approved));
        var service = CreateService(repository, organizationId, Roles.Admin);

        var result = await service.ChangeStatusAsync(
            repository.Job!.Id,
            new ChangeJobStatusRequest(JobStatus.InReview),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, result.Status);
        Assert.Contains("invalid_job_status_transition", result.Errors);
        Assert.Equal(1, repository.GetSingleCalls);
        Assert.Equal(0, repository.TransitionCalls);
    }

    [Fact]
    public async Task ChangeStatusAsync_returns_not_found_when_job_is_outside_tenant()
    {
        var repository = new StubJobRepository(null);
        var service = CreateService(repository, Guid.NewGuid(), Roles.Admin);

        var result = await service.ChangeStatusAsync(
            Guid.NewGuid(),
            new ChangeJobStatusRequest(JobStatus.Approved),
            CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(1, repository.GetSingleCalls);
        Assert.Equal(0, repository.TransitionCalls);
    }

    [Fact]
    public async Task GetSingleJobAsync_hides_a_copy_assigned_to_another_employee()
    {
        var organizationId = Guid.NewGuid();
        var currentUserId = Guid.NewGuid();
        var repository = new StubJobRepository(CreateJob(organizationId, JobStatus.Draft) with
        {
            AssignedUsers = [new AssignedUserResponse(Guid.NewGuid(), "Another employee")]
        });
        var service = CreateService(repository, organizationId, Roles.User, currentUserId);

        var result = await service.GetSingleJobAsync(repository.Job!.Id, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(1, repository.GetSingleCalls);
    }

    [Fact]
    public async Task CreateAsync_does_not_allow_regular_user_to_link_another_employees_copy()
    {
        var organizationId = Guid.NewGuid();
        var repository = new StubJobRepository(CreateJob(organizationId, JobStatus.Draft) with
        {
            AssignedUsers = [new AssignedUserResponse(Guid.NewGuid(), "Another employee")]
        });
        var service = CreateService(repository, organizationId, Roles.User, Guid.NewGuid());

        var result = await service.CreateAsync(
            new CreateJobRequest(LinkedJobIds: [repository.Job!.Id]),
            CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(1, repository.GetSingleCalls);
    }

    [Fact]
    public async Task UpdateAsync_hides_a_copy_assigned_to_another_employee()
    {
        var organizationId = Guid.NewGuid();
        var currentUserId = Guid.NewGuid();
        var repository = new StubJobRepository(CreateJob(organizationId, JobStatus.Draft) with
        {
            AssignedUsers = [new AssignedUserResponse(Guid.NewGuid(), "Another employee")]
        });
        var service = CreateService(repository, organizationId, Roles.User, currentUserId);

        var result = await service.UpdateAsync(
            repository.Job!.Id,
            new UpdateJobRequest(),
            CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(1, repository.GetSingleCalls);
    }

    [Fact]
    public async Task ChangeStatusAsync_hides_a_copy_assigned_to_another_employee_before_transition()
    {
        var organizationId = Guid.NewGuid();
        var repository = new StubJobRepository(CreateJob(organizationId, JobStatus.Draft) with
        {
            AssignedUsers = [new AssignedUserResponse(Guid.NewGuid(), "Another employee")]
        });
        var service = CreateService(repository, organizationId, Roles.User, Guid.NewGuid());

        var result = await service.ChangeStatusAsync(
            repository.Job!.Id,
            new ChangeJobStatusRequest(JobStatus.InReview),
            CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(1, repository.GetSingleCalls);
        Assert.Equal(0, repository.TransitionCalls);
    }

    [Fact]
    public async Task MarkJobAsSeenAsync_hides_a_copy_assigned_to_another_employee()
    {
        var organizationId = Guid.NewGuid();
        var repository = new StubJobRepository(CreateJob(organizationId, JobStatus.Draft) with
        {
            AssignedUsers = [new AssignedUserResponse(Guid.NewGuid(), "Another employee")]
        });
        var service = CreateService(repository, organizationId, Roles.User, Guid.NewGuid());

        var result = await service.MarkJobAsSeenAsync(repository.Job!.Id, JobViewTypes.New, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(1, repository.GetSingleCalls);
    }

    [Fact]
    public async Task GetHistoryAsync_hides_a_copy_assigned_to_another_employee()
    {
        var organizationId = Guid.NewGuid();
        var repository = new StubJobRepository(CreateJob(organizationId, JobStatus.Draft) with
        {
            AssignedUsers = [new AssignedUserResponse(Guid.NewGuid(), "Another employee")]
        });
        var service = CreateService(repository, organizationId, Roles.User, Guid.NewGuid());

        var result = await service.GetHistoryAsync(repository.Job!.Id, 50, 0, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(1, repository.GetSingleCalls);
    }

    [Fact]
    public async Task CreateLinksAsync_does_not_allow_regular_user_to_link_another_employees_copy()
    {
        var organizationId = Guid.NewGuid();
        var repository = new StubJobRepository(CreateJob(organizationId, JobStatus.Draft) with
        {
            AssignedUsers = [new AssignedUserResponse(Guid.NewGuid(), "Another employee")]
        });
        var service = CreateService(repository, organizationId, Roles.User, Guid.NewGuid());

        var result = await service.CreateLinksAsync(
            repository.Job!.Id,
            new CreateJobLinkRequest([Guid.NewGuid()]),
            CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(1, repository.GetSingleCalls);
    }

    [Fact]
    public async Task DeleteLinksAsync_does_not_allow_regular_user_to_remove_links_from_another_employees_copy()
    {
        var organizationId = Guid.NewGuid();
        var repository = new StubJobRepository(CreateJob(organizationId, JobStatus.Draft) with
        {
            AssignedUsers = [new AssignedUserResponse(Guid.NewGuid(), "Another employee")]
        });
        var service = CreateService(repository, organizationId, Roles.User, Guid.NewGuid());

        var result = await service.DeleteLinksAsync(
            repository.Job!.Id,
            new DeleteJobLinksRequest([Guid.NewGuid()]),
            CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(1, repository.GetSingleCalls);
    }

    [Fact]
    public async Task MarkJobAsSeenAsync_returns_not_found_for_auditor_job_outside_discipline_scope()
    {
        var organizationId = Guid.NewGuid();
        var repository = new StubJobRepository(CreateJob(organizationId, JobStatus.Approved, "Varme"));
        var service = CreateService(repository, organizationId, Roles.Auditor);

        var result = await service.MarkJobAsSeenAsync(
            repository.Job!.Id,
            JobViewTypes.New,
            CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(1, repository.GetSingleCalls);
    }

    [Fact]
    public async Task GetHistoryAsync_returns_not_found_for_auditor_job_outside_discipline_scope()
    {
        var organizationId = Guid.NewGuid();
        var repository = new StubJobRepository(CreateJob(organizationId, JobStatus.Approved, "Ventilation"));
        var service = CreateService(repository, organizationId, Roles.Auditor);

        var result = await service.GetHistoryAsync(
            repository.Job!.Id,
            50,
            0,
            CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(1, repository.GetSingleCalls);
    }

    private static AuthorizedJobService CreateService(
        IJobRepository repository,
        Guid organizationId,
        string role,
        Guid? userId = null) =>
        new(
            inner: null!,
            jobRepository: repository,
            currentUser: new StubCurrentUserContext(userId ?? Guid.NewGuid(), organizationId, role),
            logger: NullLogger<AuthorizedJobService>.Instance);

    private static JobReportResponse CreateJob(Guid organizationId, JobStatus status, params string[] installationTypes) =>
        new(
            Id: Guid.NewGuid(),
            OrganizationId: organizationId,
            OrganizationName: "Test organization",
            OrganizationCvr: "12345678",
            Customer: null,
            ReportNumber: "1",
            DestinationAddress: null,
            DestinationZipCode: null,
            DestinationCity: null,
            Status: status,
            ReportDate: null,
            JobType: JobType.KLS,
            TaskDescription: null,
            CustomerObservations: null,
            TechnicalObservations: null,
            InstallationTypes: installationTypes.Select((name, index) => new InstallationTypeResponse(
                Guid.NewGuid(),
                name,
                index + 1,
                Array.Empty<InstallationTypeCategoryResponse>())).ToArray(),
            WorkKind: null,
            Remarks: null,
            ClosureFlags: Array.Empty<ClosureFlagResponse>(),
            Links: Array.Empty<JobLinkInfoResponse>(),
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            SubmittedAt: null,
            AssignedUsers: Array.Empty<AssignedUserResponse>(),
            Worksheets: Array.Empty<WorksheetUserGroupResponse>(),
            SoftDeleted: false,
            DeletionScheduledAt: null,
            TotalHours: null,
            RejectionNote: null);

    private sealed record StubCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

    private sealed class StubJobRepository(JobReportResponse? job) : IJobRepository
    {
        public JobReportResponse? Job { get; } = job;
        public int GetSingleCalls { get; private set; }
        public int TransitionCalls { get; private set; }

        public Task<JobReportResponse?> GetSingleJobAsync(
            Guid id,
            Guid organizationId,
            CancellationToken cancellationToken)
        {
            GetSingleCalls++;
            return Task.FromResult(
                Job is not null && Job.Id == id && Job.OrganizationId == organizationId
                    ? Job
                    : null);
        }

        public Task<JobTransitionResult?> TransitionAsync(
            Guid id,
            Guid organizationId,
            JobStatus nextStatus,
            Guid? actorId,
            string? rejectionNote,
            CancellationToken cancellationToken)
        {
            TransitionCalls++;
            throw new InvalidOperationException("A denied transition must not reach persistence.");
        }

        public Task<JobReportResponse> CreateAsync(Guid organizationId, CreateJobRequest request, IReadOnlyList<Guid> assignedUserIds, Guid? actorId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<JobListResponse> ListAsync(JobQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<JobHistoryResponse>?> GetEventsAsync(Guid id, Guid organizationId, int limit, int offset, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<JobReportResponse?> UpdateAsync(Guid id, Guid organizationId, UpdateJobRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<JobDeleteRepositoryResult> DeleteAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<JobReportResponse?> RestoreDeletionAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> PurgeDeletionScheduledBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
