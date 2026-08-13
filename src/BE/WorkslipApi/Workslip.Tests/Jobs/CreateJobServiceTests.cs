using Ardalis.Result;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Jobs.Validators;
using Workslip.Application.Notifications;
using Workslip.Application.Worksheets;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Tests.Jobs;

public sealed class CreateJobServiceTests
{
    [Fact]
    public async Task CreateAsync_passes_explicit_assignments_and_notifies_each_duplicated_job_assignee()
    {
        var organizationId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var firstUserId = Guid.NewGuid();
        var secondUserId = Guid.NewGuid();
        var linkedJobId = Guid.NewGuid();
        var repository = new RecordingCreateJobRepository(
            organizationId,
            linkedJobId,
            [
                new AssignedUserResponse(firstUserId, "Employee 1"),
                new AssignedUserResponse(secondUserId, "Employee 2")
            ]);
        var notifications = new RecordingNotificationService();
        var currentUser = new TestCurrentUserContext(adminId, organizationId, Roles.Admin);
        var worksheets = new EmptyWorksheetRepository();

        using var services = CreateCacheServices();
        var service = new JobService(
            repository,
            null!,
            null!,
            null!,
            new EmptyReferenceDataRepository(),
            null!,
            worksheets,
            services.GetRequiredService<HybridCache>(),
            new CreateJobRequestValidator(new AllowAssignmentValidator(), worksheets, currentUser),
            null!,
            null!,
            currentUser,
            NullLogger<JobService>.Instance,
            null!,
            notifications,
            null!);
        var request = new CreateJobRequest(
            JobType: JobType.Diverse.ToString(),
            AssignedUserIds: [firstUserId, secondUserId],
            DuplicatePerAssignedUser: true,
            LinkedJobIds: [linkedJobId]);

        var result = await service.CreateAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(request.AssignedUserIds, repository.LastAssignedUserIds);
        Assert.Equal(request.LinkedJobIds, repository.LastRequest?.LinkedJobIds);
        Assert.Equal(repository.CreatedJobs.Select(job => job.Id), result.Value.CreatedJobIds);
        Assert.Equal(2, notifications.Assigned.Count);
        Assert.Equal(
            repository.CreatedJobs
                .SelectMany(job => job.AssignedUsers.Select(user => (JobId: job.Id, UserId: user.Id)))
                .OrderBy(pair => pair.JobId),
            notifications.Assigned
                .Select(call => (call.JobId, call.UserId))
                .OrderBy(pair => pair.JobId));
    }

    private static ServiceProvider CreateCacheServices()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        return services.BuildServiceProvider();
    }

    private sealed record TestCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

    private sealed class RecordingCreateJobRepository(
        Guid organizationId,
        Guid linkedJobId,
        IReadOnlyList<AssignedUserResponse> assignees) : IJobRepository
    {
        private readonly JobReportResponse linkedJob = CreateJob(
            linkedJobId,
            organizationId,
            "LINK",
            assignedUser: null);

        internal CreateJobRequest? LastRequest { get; private set; }
        internal IReadOnlyList<Guid>? LastAssignedUserIds { get; private set; }
        internal IReadOnlyList<JobReportResponse> CreatedJobs { get; private set; } = [];

        public Task<JobReportResponse> CreateAsync(
            Guid requestedOrganizationId,
            CreateJobRequest request,
            IReadOnlyList<Guid> assignedUserIds,
            Guid? actorId,
            CancellationToken cancellationToken)
        {
            Assert.Equal(organizationId, requestedOrganizationId);
            LastRequest = request;
            LastAssignedUserIds = assignedUserIds.ToArray();
            CreatedJobs = assignees
                .Select((assignedUser, index) => CreateJob(
                    Guid.NewGuid(),
                    organizationId,
                    (index + 1).ToString("D4"),
                    assignedUser))
                .ToArray();
            var createdJobIds = CreatedJobs.Select(job => job.Id).ToArray();
            return Task.FromResult(CreatedJobs[0] with { CreatedJobIds = createdJobIds });
        }

        public Task<JobReportResponse?> GetSingleJobAsync(
            Guid id,
            Guid requestedOrganizationId,
            CancellationToken cancellationToken)
        {
            if (requestedOrganizationId != organizationId)
                return Task.FromResult<JobReportResponse?>(null);

            if (id == linkedJob.Id)
                return Task.FromResult<JobReportResponse?>(linkedJob);

            return Task.FromResult<JobReportResponse?>(CreatedJobs.FirstOrDefault(job => job.Id == id));
        }

        public Task<JobListResponse> ListAsync(JobQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobHistoryResponse>?> GetEventsAsync(Guid id, Guid organizationId, int limit, int offset, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobReportResponse?> UpdateAsync(Guid id, Guid organizationId, UpdateJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobTransitionResult?> TransitionAsync(Guid id, Guid organizationId, JobStatus nextStatus, Guid? actorId, string? rejectionNote, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobDeleteRepositoryResult> DeleteAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobReportResponse?> RestoreDeletionAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> PurgeDeletionScheduledBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static JobReportResponse CreateJob(
        Guid id,
        Guid organizationId,
        string reportNumber,
        AssignedUserResponse? assignedUser) =>
        new(
            Id: id,
            OrganizationId: organizationId,
            OrganizationName: "Test organization",
            OrganizationCvr: "12345678",
            Customer: null,
            ReportNumber: reportNumber,
            DestinationAddress: "Testvej 1",
            DestinationZipCode: "8000",
            DestinationCity: "Aarhus C",
            Status: JobStatus.Draft,
            ReportDate: null,
            JobType: JobType.Diverse,
            TaskDescription: "Testopgave",
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
            AssignedUsers: assignedUser is null ? [] : [assignedUser],
            Worksheets: [],
            SoftDeleted: false,
            DeletionScheduledAt: null,
            TotalHours: null,
            RejectionNote: null);

    private sealed class AllowAssignmentValidator : IJobAssignmentValidator
    {
        public Task<JobAssignmentValidationResult> ValidateForExistingJobAsync(Guid jobId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult(JobAssignmentValidationResult.Valid());

        public Task<JobAssignmentValidationResult> ValidateForDefaultFilialAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult(JobAssignmentValidationResult.Valid());
    }

    private sealed class EmptyReferenceDataRepository : IReferenceDataRepository
    {
        public Task<ReferenceDataResponse> GetAsync(Guid? organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(new ReferenceDataResponse([], [], []));
    }

    private sealed class EmptyWorksheetRepository : IWorksheetRepository
    {
        public Task<decimal> GetHoursForUserDayAsync(Guid organizationId, Guid userId, DateOnly workDate, CancellationToken cancellationToken) => Task.FromResult(0m);
        public Task<IReadOnlyList<WorksheetResponse>> ListByJobAsync(Guid jobId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WorksheetResponse>>([]);
        public Task<WorksheetResponse> UpsertAsync(UpsertWorksheetRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(Guid worksheetId, Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorksheetUserGroupResponse>> GetGroupedByJobAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, decimal?>> GetTotalHoursByJobAsync(IEnumerable<Guid> jobIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MyWorksheetEntryResponse>> GetWorksheetsForUserAsync(Guid userId, Guid organizationId, DateOnly monthStart, DateOnly monthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MyWorksheetEntryResponse>> GetAllWorksheetsAsync(Guid organizationId, DateOnly monthStart, DateOnly monthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class RecordingNotificationService : INotificationService
    {
        internal List<AssignedCall> Assigned { get; } = [];

        public Task QueueJobAssignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken)
        {
            Assigned.Add(new AssignedCall(userId, jobId));
            return Task.CompletedTask;
        }

        public Task QueueJobReadyForReviewAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobDeniedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, string? rejectionNote, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobCompletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobUnassignedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task QueueJobDeletedAsync(Guid userId, string recipientName, Guid jobId, string jobNumber, string customerAddress, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Result> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
        public (string Title, string Body) GetLocalizedText(NotificationType notificationType, string jobNumber, string customerAddress, string recipientName, string? rejectionNote = null) => ("", "");
    }

    private sealed record AssignedCall(Guid UserId, Guid JobId);
}
