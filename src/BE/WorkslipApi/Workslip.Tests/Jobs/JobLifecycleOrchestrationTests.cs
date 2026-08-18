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

public sealed class JobLifecycleOrchestrationTests
{
    [Fact]
    public async Task DuplicateTransition_ReturnsSuccessWithoutRepeatingSideEffects()
    {
        var organizationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var repository = new RecordingJobRepository(
            CreateJob(organizationId, JobStatus.InReview),
            changed: false);
        var assignments = new RecordingAssignmentRepository();
        var notifications = new RecordingNotificationService();
        var views = new RecordingJobViewRepository();

        using var services = CreateServices();
        var service = CreateService(
            repository,
            assignments,
            notifications,
            views,
            services.GetRequiredService<HybridCache>(),
            new TestCurrentUserContext(actorId, organizationId, Roles.Admin));

        var result = await service.ChangeStatusAsync(
            repository.Job.Id,
            new ChangeJobStatusRequest(JobStatus.InReview),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, repository.TransitionCalls);
        Assert.Empty(assignments.RequestedAdminLookups);
        Assert.Empty(notifications.ReadyForReview);
        Assert.Empty(notifications.Completed);
        Assert.Empty(notifications.Denied);
        Assert.Empty(views.Marked);
    }

    [Fact]
    public async Task SubmitForReview_NotifiesOtherAdminsButNotActor()
    {
        var organizationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var otherAdminId = Guid.NewGuid();
        var repository = new RecordingJobRepository(
            CreateJob(organizationId, JobStatus.Draft),
            changed: true);
        var assignments = new RecordingAssignmentRepository
        {
            OrganizationAdmins =
            [
                new AssignedUserResponse(actorId, "Acting admin"),
                new AssignedUserResponse(otherAdminId, "Review admin")
            ]
        };
        var notifications = new RecordingNotificationService();
        var views = new RecordingJobViewRepository();

        using var services = CreateServices();
        var service = CreateService(
            repository,
            assignments,
            notifications,
            views,
            services.GetRequiredService<HybridCache>(),
            new TestCurrentUserContext(actorId, organizationId, Roles.Admin));

        var result = await service.ChangeStatusAsync(
            repository.Job.Id,
            new ChangeJobStatusRequest(JobStatus.InReview),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(JobStatus.InReview, repository.Job.Status);
        Assert.Equal([organizationId], assignments.RequestedAdminLookups);
        var ready = Assert.Single(notifications.ReadyForReview);
        Assert.Equal(otherAdminId, ready.UserId);
        Assert.DoesNotContain(notifications.ReadyForReview, call => call.UserId == actorId);
        Assert.Empty(views.Marked);
    }

    [Fact]
    public async Task Approval_NotifiesOtherAssigneesAndMarksCompletedViewForActor()
    {
        var organizationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var assigneeId = Guid.NewGuid();
        var job = CreateJob(
            organizationId,
            JobStatus.InReview,
            [
                new AssignedUserResponse(actorId, "Acting admin"),
                new AssignedUserResponse(assigneeId, "Montør")
            ]);
        var repository = new RecordingJobRepository(job, changed: true);
        var assignments = new RecordingAssignmentRepository();
        var notifications = new RecordingNotificationService();
        var views = new RecordingJobViewRepository();

        using var services = CreateServices();
        var service = CreateService(
            repository,
            assignments,
            notifications,
            views,
            services.GetRequiredService<HybridCache>(),
            new TestCurrentUserContext(actorId, organizationId, Roles.Admin));

        var result = await service.ChangeStatusAsync(
            repository.Job.Id,
            new ChangeJobStatusRequest(JobStatus.Approved),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(JobStatus.Approved, repository.Job.Status);
        var completed = Assert.Single(notifications.Completed);
        Assert.Equal(assigneeId, completed.UserId);
        Assert.DoesNotContain(notifications.Completed, call => call.UserId == actorId);
        var marked = Assert.Single(views.Marked);
        Assert.Equal(repository.Job.Id, marked.JobId);
        Assert.Equal(actorId, marked.UserId);
        Assert.Equal(JobViewTypes.Completed, marked.ViewType);
    }

    private static ServiceProvider CreateServices()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        return services.BuildServiceProvider();
    }

    private static JobLifecycleService CreateService(
        IJobRepository repository,
        IAssignmentRepository assignments,
        INotificationService notifications,
        IJobViewRepository views,
        HybridCache cache,
        ICurrentUserContext currentUser) =>
        new(
            repository,
            views,
            assignments,
            new EmptyReferenceDataRepository(),
            new EmptyWorksheetRepository(),
            cache,
            new InlineValidator<ChangeJobStatusRequest>(),
            currentUser,
            NullLogger<JobService>.Instance,
            new JobValidationService(NullLogger<JobValidationService>.Instance),
            notifications);

    private static JobReportResponse CreateJob(
        Guid organizationId,
        JobStatus status,
        IReadOnlyList<AssignedUserResponse>? assignedUsers = null) =>
        new(
            Id: Guid.NewGuid(),
            OrganizationId: organizationId,
            OrganizationName: "Test organization",
            OrganizationCvr: "12345678",
            Customer: null,
            ReportNumber: "0001",
            DestinationAddress: "Testvej 1",
            DestinationZipCode: "8000",
            DestinationCity: "Aarhus C",
            Status: status,
            ReportDate: null,
            JobType: JobType.Diverse,
            TaskDescription: "Lifecycle test",
            CustomerObservations: null,
            TechnicalObservations: null,
            InstallationTypes: [],
            WorkKind: null,
            Remarks: null,
            ClosureFlags: [],
            Links: [],
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            SubmittedAt: null,
            AssignedUsers: assignedUsers ?? [],
            Worksheets: [],
            SoftDeleted: false,
            DeletionScheduledAt: null,
            TotalHours: null,
            RejectionNote: null);

    private sealed record TestCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

    private sealed class RecordingJobRepository(JobReportResponse job, bool changed) : IJobRepository
    {
        public JobReportResponse Job { get; private set; } = job;
        public int TransitionCalls { get; private set; }

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
            TransitionCalls++;
            if (changed)
            {
                Job = Job with
                {
                    Status = nextStatus,
                    RejectionNote = rejectionNote
                };
            }

            return Task.FromResult<JobTransitionResult?>(
                new JobTransitionResult(Job, changed, SubmittedByUserId: null));
        }

        public Task<JobReportResponse> CreateAsync(Guid organizationId, CreateJobRequest request, IReadOnlyList<Guid> assignedUserIds, Guid? actorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobListResponse> ListAsync(JobQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobHistoryResponse>?> GetEventsAsync(Guid id, Guid organizationId, int limit, int offset, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobReportResponse?> UpdateAsync(Guid id, Guid organizationId, UpdateJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobDeleteRepositoryResult> DeleteAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobReportResponse?> RestoreDeletionAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> PurgeDeletionScheduledBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingAssignmentRepository : IAssignmentRepository
    {
        public IReadOnlyList<AssignedUserResponse> OrganizationAdmins { get; init; } = [];
        public List<Guid> RequestedAdminLookups { get; } = [];

        public Task<IReadOnlyList<AssignedUserResponse>> GetOrganizationAdminsAsync(
            Guid organizationId,
            CancellationToken cancellationToken)
        {
            RequestedAdminLookups.Add(organizationId);
            return Task.FromResult(OrganizationAdmins);
        }

        public Task AssignAsync(Guid jobId, Guid organizationId, IReadOnlyList<Guid> userIds, Guid? actorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AssignedUserResponse>> GetAssignedUsersByIdsAsync(Guid organizationId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobListItemResponse>> GetMyAssignedJobsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<AssignedUserResponse>>> GetAssignedUsersByReportAsync(Guid organizationId, IEnumerable<Guid> reportIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task AddAssignedUsersAsync(Guid organizationId, Guid reportId, IReadOnlyList<Guid> userIds, Guid? actorId, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingJobViewRepository : IJobViewRepository
    {
        public List<MarkedView> Marked { get; } = [];

        public Task MarkAsViewedAsync(
            Guid jobId,
            Guid userId,
            string viewType,
            CancellationToken cancellationToken)
        {
            Marked.Add(new MarkedView(jobId, userId, viewType));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Guid>> GetViewedJobIdsAsync(Guid userId, IReadOnlyList<Guid> jobIds, IReadOnlyList<string> viewTypes, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
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

        public Task<decimal> GetHoursForUserDayAsync(Guid organizationId, Guid userId, DateOnly workDate, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorksheetResponse> UpsertAsync(UpsertWorksheetRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(Guid worksheetId, Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorksheetUserGroupResponse>> GetGroupedByJobAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, decimal?>> GetTotalHoursByJobAsync(IEnumerable<Guid> jobIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MyWorksheetEntryResponse>> GetWorksheetsForUserAsync(Guid userId, Guid organizationId, DateOnly monthStart, DateOnly monthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MyWorksheetEntryResponse>> GetAllWorksheetsAsync(Guid organizationId, DateOnly monthStart, DateOnly monthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        public List<NotificationCall> ReadyForReview { get; } = [];
        public List<NotificationCall> Completed { get; } = [];
        public List<NotificationCall> Denied { get; } = [];

        public Task QueueJobReadyForReviewAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken)
        {
            ReadyForReview.Add(new NotificationCall(userId, jobId));
            return Task.CompletedTask;
        }

        public Task QueueJobCompletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken)
        {
            Completed.Add(new NotificationCall(userId, jobId));
            return Task.CompletedTask;
        }

        public Task QueueJobDeniedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, string? rejectionNote, CancellationToken cancellationToken)
        {
            Denied.Add(new NotificationCall(userId, jobId));
            return Task.CompletedTask;
        }

        public Task QueueJobAssignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobUnassignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobDeletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Result> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
        public (string Title, string Body) GetLocalizedText(NotificationType notificationType, string jobNumber, string customerAddress, string recipientName, string? rejectionNote = null) => (string.Empty, string.Empty);
    }

    private sealed record NotificationCall(Guid UserId, Guid JobId);
    private sealed record MarkedView(Guid JobId, Guid UserId, string ViewType);
}
