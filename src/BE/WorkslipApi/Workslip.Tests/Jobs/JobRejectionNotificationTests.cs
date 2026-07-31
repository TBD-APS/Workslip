using Ardalis.Result;
using FluentValidation;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Notifications;
using Workslip.Application.Worksheets;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Jobs;

public sealed class JobRejectionNotificationTests
{
    [Fact]
    public async Task RejectingJob_ReassignsAndNotifiesPersistedSubmitter_WithoutReadingHistory()
    {
        var organizationId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var submitterId = Guid.NewGuid();
        var repository = new RejectionJobRepository(
            CreateJob(organizationId, JobStatus.InReview, [new AssignedUserResponse(adminId, "Admin")]),
            submitterId);
        var assignments = new RecordingAssignmentRepository(
            organizationId,
            new AssignedUserResponse(submitterId, "Montør"));
        var notifications = new RecordingNotificationService();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddHybridCache();
        using var services = serviceCollection.BuildServiceProvider();
        var service = CreateService(
            repository,
            assignments,
            notifications,
            services.GetRequiredService<HybridCache>(),
            new TestCurrentUserContext(adminId, organizationId, Roles.Admin));

        var result = await service.ChangeStatusAsync(
            repository.Job.Id,
            new ChangeJobStatusRequest(JobStatus.Rejected, "Ret dokumentationen"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, repository.GetEventsCalls);
        Assert.Equal([submitterId], assignments.LastAssignedUserIds);
        var denied = Assert.Single(notifications.Denied);
        Assert.Equal(submitterId, denied.UserId);
        Assert.Equal("Montør", denied.RecipientName);
        Assert.Equal("Ret dokumentationen", denied.RejectionNote);
    }

    [Fact]
    public async Task RejectingLegacyJob_UsesBoundedCurrentAssigneeFallback()
    {
        var organizationId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var assignedUserId = Guid.NewGuid();
        var repository = new RejectionJobRepository(
            CreateJob(organizationId, JobStatus.InReview, [new AssignedUserResponse(assignedUserId, "Montør")]),
            submittedByUserId: null);
        var assignments = new RecordingAssignmentRepository(organizationId, submitter: null);
        var notifications = new RecordingNotificationService();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddHybridCache();
        using var services = serviceCollection.BuildServiceProvider();
        var service = CreateService(
            repository,
            assignments,
            notifications,
            services.GetRequiredService<HybridCache>(),
            new TestCurrentUserContext(adminId, organizationId, Roles.Admin));

        var result = await service.ChangeStatusAsync(
            repository.Job.Id,
            new ChangeJobStatusRequest(JobStatus.Rejected),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, repository.GetEventsCalls);
        Assert.Null(assignments.LastAssignedUserIds);
        Assert.Equal(assignedUserId, Assert.Single(notifications.Denied).UserId);
    }

    private static JobService CreateService(
        IJobRepository repository,
        IAssignmentRepository assignments,
        INotificationService notifications,
        HybridCache cache,
        ICurrentUserContext currentUser) =>
        new(
            repository,
            null!,
            assignments,
            null!,
            new EmptyReferenceDataRepository(),
            null!,
            new EmptyWorksheetRepository(),
            cache,
            null!,
            null!,
            new InlineValidator<ChangeJobStatusRequest>(),
            currentUser,
            NullLogger<JobService>.Instance,
            new JobValidationService(NullLogger<JobValidationService>.Instance),
            notifications,
            null!);

    private static JobReportResponse CreateJob(
        Guid organizationId,
        JobStatus status,
        IReadOnlyList<AssignedUserResponse> assignedUsers) =>
        new(
            Guid.NewGuid(),
            organizationId,
            "Test organization",
            "12345678",
            null,
            "0001",
            "Testvej 1",
            "8000",
            "Aarhus C",
            status,
            null,
            JobType.Diverse,
            null,
            null,
            null,
            [],
            null,
            null,
            [],
            [],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            assignedUsers,
            [],
            false,
            null,
            null,
            null);

    private sealed record TestCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

    private sealed class RejectionJobRepository(
        JobReportResponse job,
        Guid? submittedByUserId) : IJobRepository
    {
        public JobReportResponse Job { get; private set; } = job;
        public int GetEventsCalls { get; private set; }

        public Task<JobReportResponse?> GetSingleJobAsync(
            Guid id,
            Guid organizationId,
            CancellationToken cancellationToken) =>
            Task.FromResult<JobReportResponse?>(
                id == Job.Id && organizationId == Job.OrganizationId ? Job : null);

        public Task<JobTransitionResult?> TransitionAsync(
            Guid id,
            Guid organizationId,
            JobStatus nextStatus,
            Guid? actorId,
            string? rejectionNote,
            CancellationToken cancellationToken)
        {
            Job = Job with { Status = nextStatus, RejectionNote = rejectionNote };
            return Task.FromResult<JobTransitionResult?>(
                new JobTransitionResult(Job, true, submittedByUserId));
        }

        public Task<IReadOnlyList<JobHistoryResponse>?> GetEventsAsync(
            Guid id,
            Guid organizationId,
            int limit,
            int offset,
            CancellationToken cancellationToken)
        {
            GetEventsCalls++;
            throw new InvalidOperationException("Rejection routing must not read presentation history.");
        }

        public Task<JobReportResponse> CreateAsync(Guid organizationId, CreateJobRequest request, IReadOnlyList<Guid> assignedUserIds, Guid? actorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobListResponse> ListAsync(JobQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobReportResponse?> UpdateAsync(Guid id, Guid organizationId, UpdateJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobDeleteRepositoryResult> DeleteAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobReportResponse?> RestoreDeletionAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> PurgeDeletionScheduledBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingAssignmentRepository(
        Guid organizationId,
        AssignedUserResponse? submitter) : IAssignmentRepository
    {
        public IReadOnlyList<Guid>? LastAssignedUserIds { get; private set; }

        public Task AssignAsync(Guid jobId, Guid requestedOrganizationId, IReadOnlyList<Guid> userIds, Guid? actorId, CancellationToken cancellationToken)
        {
            Assert.Equal(organizationId, requestedOrganizationId);
            LastAssignedUserIds = userIds.ToArray();
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AssignedUserResponse>> GetAssignedUsersByIdsAsync(
            Guid requestedOrganizationId,
            IReadOnlyList<Guid> userIds,
            CancellationToken cancellationToken)
        {
            Assert.Equal(organizationId, requestedOrganizationId);
            IReadOnlyList<AssignedUserResponse> result = submitter is not null && userIds.Contains(submitter.Id)
                ? [submitter]
                : [];
            return Task.FromResult(result);
        }

        public Task<IReadOnlyList<JobListItemResponse>> GetMyAssignedJobsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<AssignedUserResponse>>> GetAssignedUsersByReportAsync(Guid organizationId, IEnumerable<Guid> reportIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAssignedUsersAsync(Guid organizationId, Guid reportId, IReadOnlyList<Guid> userIds, Guid? actorId, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class EmptyReferenceDataRepository : IReferenceDataRepository
    {
        public Task<ReferenceDataResponse> GetAsync(Guid? organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(new ReferenceDataResponse([], [], []));
    }

    private sealed class EmptyWorksheetRepository : IWorksheetRepository
    {
        public Task<IReadOnlyList<WorksheetResponse>> ListByJobAsync(Guid jobId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WorksheetResponse>>([]);

        public Task<WorksheetResponse> UpsertAsync(UpsertWorksheetRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(Guid worksheetId, Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorksheetUserGroupResponse>> GetGroupedByJobAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, decimal?>> GetTotalHoursByJobAsync(IEnumerable<Guid> jobIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MyWorksheetEntryResponse>> GetWorksheetsForUserAsync(Guid userId, Guid organizationId, DateOnly monthStart, DateOnly monthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MyWorksheetEntryResponse>> GetAllWorksheetsAsync(Guid organizationId, DateOnly monthStart, DateOnly monthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        public List<DeniedCall> Denied { get; } = [];

        public Task QueueJobDeniedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, string? rejectionNote, CancellationToken cancellationToken)
        {
            Denied.Add(new DeniedCall(userId, recipientName, rejectionNote));
            return Task.CompletedTask;
        }

        public Task QueueJobAssignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobReadyForReviewAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobCompletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobUnassignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobDeletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Result> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
        public (string Title, string Body) GetLocalizedText(NotificationType notificationType, string jobNumber, string customerAddress, string recipientName, string? rejectionNote = null) => ("", "");
    }

    private sealed record DeniedCall(Guid UserId, string RecipientName, string? RejectionNote);
}
