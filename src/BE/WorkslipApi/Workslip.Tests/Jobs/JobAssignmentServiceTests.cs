using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Notifications;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Jobs;

public sealed class JobAssignmentServiceTests
{
    [Fact]
    public async Task AssignAsync_valid_assignment_mutates_invalidates_and_notifies_assignee()
    {
        var organizationId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var assigneeId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var assignments = new RecordingAssignmentRepository();
        var jobs = new RecordingJobService(CreateSummary(
            jobId,
            organizationId,
            [new AssignedUserResponse(assigneeId, "Montør")]));
        var notifications = new RecordingNotificationService();
        var service = new JobAssignmentService(
            new StubValidator(JobAssignmentValidationResult.Valid()),
            assignments,
            jobs,
            new TestCurrentUserContext(adminId, organizationId, Roles.Admin),
            notifications,
            NullLogger<JobAssignmentService>.Instance);

        var result = await service.AssignAsync(jobId, [assigneeId], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(jobId, assignments.AssignedJobId);
        Assert.Equal(organizationId, assignments.AssignedOrganizationId);
        Assert.Equal([assigneeId], assignments.AssignedUserIds);
        Assert.Equal(adminId, assignments.AssignedActorId);
        Assert.Equal((jobId, organizationId), jobs.Invalidated);
        Assert.Equal(1, jobs.GetSingleCalls);
        var notification = Assert.Single(notifications.Assigned);
        Assert.Equal(assigneeId, notification.UserId);
        Assert.Equal("Montør", notification.RecipientName);
        Assert.Equal("R-1", notification.JobNumber);
        Assert.Equal("Jobvej 2", notification.Address);
    }

    [Fact]
    public async Task AssignAsync_invalid_assignee_does_not_mutate_or_notify()
    {
        var organizationId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var assignments = new RecordingAssignmentRepository();
        var jobs = new RecordingJobService(CreateSummary(jobId, organizationId, []));
        var notifications = new RecordingNotificationService();
        var service = new JobAssignmentService(
            new StubValidator(JobAssignmentValidationResult.InvalidAssignee()),
            assignments,
            jobs,
            new TestCurrentUserContext(adminId, organizationId, Roles.Admin),
            notifications,
            NullLogger<JobAssignmentService>.Instance);

        var result = await service.AssignAsync(jobId, [Guid.NewGuid()], CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Null(assignments.AssignedJobId);
        Assert.Null(jobs.Invalidated);
        Assert.Equal(0, jobs.GetSingleCalls);
        Assert.Empty(notifications.Assigned);
        Assert.Empty(notifications.Unassigned);
    }

    [Fact]
    public async Task AssignAsync_empty_assignment_notifies_other_admins()
    {
        var organizationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var otherAdminId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var assignments = new RecordingAssignmentRepository
        {
            Admins =
            [
                new AssignedUserResponse(actorId, "Aktør"),
                new AssignedUserResponse(otherAdminId, "Anden admin")
            ]
        };
        var jobs = new RecordingJobService(CreateSummary(jobId, organizationId, []));
        var notifications = new RecordingNotificationService();
        var service = new JobAssignmentService(
            new StubValidator(JobAssignmentValidationResult.Valid()),
            assignments,
            jobs,
            new TestCurrentUserContext(actorId, organizationId, Roles.Admin),
            notifications,
            NullLogger<JobAssignmentService>.Instance);

        var result = await service.AssignAsync(jobId, [], CancellationToken.None);

        Assert.True(result.IsSuccess);
        var notification = Assert.Single(notifications.Unassigned);
        Assert.Equal(otherAdminId, notification.UserId);
        Assert.Equal("Anden admin", notification.RecipientName);
        Assert.Equal(1, assignments.GetOrganizationAdminsCalls);
    }

    private static JobReportSummaryResponse CreateSummary(
        Guid jobId,
        Guid organizationId,
        IReadOnlyList<AssignedUserResponse> assignedUsers) =>
        new(
            jobId,
            organizationId,
            "Test organization",
            "12345678",
            "R-1",
            JobStatus.Draft,
            null,
            new CustomerSnapshotResponse("Kunde", null, null, "Kundevej 1", null),
            "Jobvej 2",
            null,
            null,
            JobType.Diverse.ToString(),
            new JobReportSummaryWorkResponse(null, [], [], null),
            new JobReportSummaryObservationResponse(null, null, null),
            [],
            [],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            null,
            assignedUsers,
            [],
            null,
            null,
            false,
            null);

    private sealed record TestCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

    private sealed class StubValidator(JobAssignmentValidationResult result) : IJobAssignmentValidator
    {
        public Task<JobAssignmentValidationResult> ValidateForExistingJobAsync(
            Guid jobId,
            IReadOnlyList<Guid> userIds,
            CancellationToken cancellationToken) => Task.FromResult(result);

        public Task<JobAssignmentValidationResult> ValidateForDefaultFilialAsync(
            IReadOnlyList<Guid> userIds,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingAssignmentRepository : IAssignmentRepository
    {
        public Guid? AssignedJobId { get; private set; }
        public Guid? AssignedOrganizationId { get; private set; }
        public IReadOnlyList<Guid>? AssignedUserIds { get; private set; }
        public Guid? AssignedActorId { get; private set; }
        public IReadOnlyList<AssignedUserResponse> Admins { get; init; } = [];
        public int GetOrganizationAdminsCalls { get; private set; }

        public Task AssignAsync(
            Guid jobId,
            Guid organizationId,
            IReadOnlyList<Guid> userIds,
            Guid? actorId,
            CancellationToken cancellationToken)
        {
            AssignedJobId = jobId;
            AssignedOrganizationId = organizationId;
            AssignedUserIds = userIds.ToArray();
            AssignedActorId = actorId;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AssignedUserResponse>> GetOrganizationAdminsAsync(
            Guid organizationId,
            CancellationToken cancellationToken)
        {
            GetOrganizationAdminsCalls++;
            return Task.FromResult(Admins);
        }

        public Task<IReadOnlyList<JobListItemResponse>> GetMyAssignedJobsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<AssignedUserResponse>>> GetAssignedUsersByReportAsync(Guid organizationId, IEnumerable<Guid> reportIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AssignedUserResponse>> GetAssignedUsersByIdsAsync(Guid organizationId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAssignedUsersAsync(Guid organizationId, Guid reportId, IReadOnlyList<Guid> userIds, Guid? actorId, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingJobService(JobReportSummaryResponse summary) : IJobService
    {
        public (Guid JobId, Guid OrganizationId)? Invalidated { get; private set; }
        public int GetSingleCalls { get; private set; }

        public Task InvalidateJobDetailCacheAsync(Guid id, Guid organizationId, CancellationToken cancellationToken)
        {
            Invalidated = (id, organizationId);
            return Task.CompletedTask;
        }

        public Task<Result<JobReportSummaryResponse>> GetSingleJobAsync(Guid id, CancellationToken cancellationToken)
        {
            GetSingleCalls++;
            return Task.FromResult(Result<JobReportSummaryResponse>.Success(summary));
        }

        public Task<Result<JobReportSummaryResponse>> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobListResponse>> ListAsync(List<JobStatus>? statuses, string? reportNumber, string? customerName, string? customerEmail, string? customerAddress, string? search, string? sortBy, string? sortDirection, int? limit, int? offset, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<JobListItemResponse>>> GetMyAssignedJobsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<JobHistoryResponse>>> GetHistoryAsync(Guid id, int? limit, int? offset, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> ChangeStatusAsync(Guid id, ChangeJobStatusRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> CreateLinksAsync(Guid reportId, CreateJobLinkRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> DeleteLinksAsync(Guid reportId, DeleteJobLinksRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobDeleteErrorResponse>> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> RestoreDeletionAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> MarkJobAsSeenAsync(Guid id, string? viewType, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        public List<NotificationCall> Assigned { get; } = [];
        public List<NotificationCall> Unassigned { get; } = [];

        public Task QueueJobAssignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken)
        {
            Assigned.Add(new NotificationCall(userId, recipientName, jobId, jobNumber, customerAddress));
            return Task.CompletedTask;
        }

        public Task QueueJobUnassignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken)
        {
            Unassigned.Add(new NotificationCall(userId, recipientName, jobId, jobNumber, customerAddress));
            return Task.CompletedTask;
        }

        public Task QueueJobReadyForReviewAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task QueueJobDeniedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, string? rejectionNote, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task QueueJobCompletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task QueueJobDeletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public (string Title, string Body) GetLocalizedText(NotificationType notificationType, string jobNumber, string customerAddress, string recipientName, string? rejectionNote = null) => throw new NotSupportedException();
    }

    private sealed record NotificationCall(
        Guid UserId,
        string RecipientName,
        Guid JobId,
        string JobNumber,
        string Address);
}
