using Ardalis.Result;
using FluentValidation;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Worksheets;
using Workslip.Domain;

namespace Workslip.Tests.Jobs;

public sealed class JobLifecycleServiceTests
{
    [Fact]
    public async Task ChangeStatusAsync_WithoutOrganizationContext_RemainsUnauthorized()
    {
        var service = new JobLifecycleService(
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            new InlineValidator<ChangeJobStatusRequest>(),
            new TestCurrentUserContext(Guid.NewGuid(), null, Roles.Admin),
            NullLogger<JobService>.Instance,
            null!,
            null!);

        var result = await service.ChangeStatusAsync(
            Guid.NewGuid(),
            new ChangeJobStatusRequest(JobStatus.Approved),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
    }

    [Fact]
    public async Task ChangeStatusAsync_Approval_DoesNotRevalidateSubmitReadiness()
    {
        var organizationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var repository = new RecordingLifecycleJobRepository(CreateJob(jobId, organizationId, JobStatus.InReview));
        var referenceData = new CountingReferenceDataRepository();
        var worksheets = new EmptyWorksheetRepository();
        var jobViews = new RecordingJobViewRepository();

        using var services = CreateCacheServices();
        var service = CreateService(
            repository,
            jobViews,
            referenceData,
            worksheets,
            services.GetRequiredService<HybridCache>(),
            new TestCurrentUserContext(actorId, organizationId, Roles.Admin));

        var result = await service.ChangeStatusAsync(
            jobId,
            new ChangeJobStatusRequest(JobStatus.Approved),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(JobStatus.Approved, result.Value.Status);
        Assert.Equal(JobStatus.Approved, repository.LastTargetStatus);
        Assert.Equal(1, referenceData.GetCalls);
        Assert.NotNull(jobViews.LastMarkedView);
        Assert.Equal(jobId, jobViews.LastMarkedView.Value.JobId);
        Assert.Equal(actorId, jobViews.LastMarkedView.Value.UserId);
        Assert.Equal(JobViewTypes.Completed, jobViews.LastMarkedView.Value.ViewType);
    }

    [Fact]
    public async Task ChangeStatusAsync_InReviewReplay_DoesNotRevalidateSubmitReadiness()
    {
        var organizationId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var repository = new RecordingLifecycleJobRepository(CreateJob(jobId, organizationId, JobStatus.InReview));
        var referenceData = new CountingReferenceDataRepository();
        var worksheets = new EmptyWorksheetRepository();
        var jobViews = new RecordingJobViewRepository();

        using var services = CreateCacheServices();
        var service = CreateService(
            repository,
            jobViews,
            referenceData,
            worksheets,
            services.GetRequiredService<HybridCache>(),
            new TestCurrentUserContext(actorId, organizationId, Roles.Admin));

        var result = await service.ChangeStatusAsync(
            jobId,
            new ChangeJobStatusRequest(JobStatus.InReview),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(JobStatus.InReview, result.Value.Status);
        Assert.Equal(JobStatus.InReview, repository.LastTargetStatus);
        Assert.Equal(1, referenceData.GetCalls);
        Assert.Null(jobViews.LastMarkedView);
    }

    private static JobLifecycleService CreateService(
        IJobRepository repository,
        IJobViewRepository jobViews,
        IReferenceDataRepository referenceData,
        IWorksheetRepository worksheets,
        HybridCache cache,
        ICurrentUserContext currentUser) =>
        new(
            repository,
            jobViews,
            null!,
            referenceData,
            worksheets,
            cache,
            new InlineValidator<ChangeJobStatusRequest>(),
            currentUser,
            NullLogger<JobService>.Instance,
            new JobValidationService(NullLogger<JobValidationService>.Instance),
            null!);

    private static ServiceProvider CreateCacheServices()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        return services.BuildServiceProvider();
    }

    private static JobReportResponse CreateJob(Guid id, Guid organizationId, JobStatus status) =>
        new(
            Id: id,
            OrganizationId: organizationId,
            OrganizationName: "Test organization",
            OrganizationCvr: "12345678",
            Customer: null,
            ReportNumber: "0042",
            DestinationAddress: "Testvej 1",
            DestinationZipCode: "8000",
            DestinationCity: "Aarhus C",
            Status: status,
            ReportDate: null,
            JobType: JobType.KLS,
            TaskDescription: null,
            CustomerObservations: null,
            TechnicalObservations: null,
            InstallationTypes: [],
            WorkKind: null,
            Remarks: null,
            ClosureFlags: [],
            Links: [],
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            SubmittedAt: DateTimeOffset.UtcNow,
            AssignedUsers: [],
            Worksheets: [],
            SoftDeleted: false,
            DeletionScheduledAt: null,
            TotalHours: null,
            RejectionNote: null);

    private sealed record TestCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

    private sealed class RecordingLifecycleJobRepository(JobReportResponse job) : IJobRepository
    {
        internal JobStatus? LastTargetStatus { get; private set; }

        public Task<JobReportResponse?> GetSingleJobAsync(
            Guid id,
            Guid organizationId,
            CancellationToken cancellationToken) =>
            Task.FromResult<JobReportResponse?>(
                id == job.Id && organizationId == job.OrganizationId ? job : null);

        public Task<JobTransitionResult?> TransitionAsync(
            Guid id,
            Guid organizationId,
            JobStatus nextStatus,
            Guid? actorId,
            string? rejectionNote,
            CancellationToken cancellationToken)
        {
            if (id != job.Id || organizationId != job.OrganizationId)
                return Task.FromResult<JobTransitionResult?>(null);

            LastTargetStatus = nextStatus;
            var changed = job.Status != nextStatus;
            var transitioned = changed
                ? job with
                {
                    Status = nextStatus,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    RejectionNote = nextStatus is JobStatus.Rejected or JobStatus.Reopened ? rejectionNote : null
                }
                : job;
            return Task.FromResult<JobTransitionResult?>(new JobTransitionResult(transitioned, changed, actorId));
        }

        public Task<JobReportResponse> CreateAsync(Guid organizationId, CreateJobRequest request, IReadOnlyList<Guid> assignedUserIds, Guid? actorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobListResponse> ListAsync(JobQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobHistoryResponse>?> GetEventsAsync(Guid id, Guid organizationId, int limit, int offset, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobReportResponse?> UpdateAsync(Guid id, Guid organizationId, UpdateJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobDeleteRepositoryResult> DeleteAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobReportResponse?> RestoreDeletionAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> PurgeDeletionScheduledBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CountingReferenceDataRepository : IReferenceDataRepository
    {
        internal int GetCalls { get; private set; }

        public Task<ReferenceDataResponse> GetAsync(Guid? organizationId, CancellationToken cancellationToken)
        {
            GetCalls++;
            return Task.FromResult(new ReferenceDataResponse([], [], []));
        }
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

    private sealed class RecordingJobViewRepository : IJobViewRepository
    {
        internal (Guid JobId, Guid UserId, string ViewType)? LastMarkedView { get; private set; }

        public Task MarkAsViewedAsync(Guid jobId, Guid userId, string viewType, CancellationToken cancellationToken)
        {
            LastMarkedView = (jobId, userId, viewType);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Guid>> GetViewedJobIdsAsync(Guid userId, IReadOnlyList<Guid> jobIds, IReadOnlyList<string> viewTypes, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}
