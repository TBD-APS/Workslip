using Ardalis.Result;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Worksheets;
using Workslip.Domain;

namespace Workslip.Tests.Worksheets;

public sealed class WorksheetServiceAuthorizationTests
{
    [Fact]
    public async Task UpsertAsync_forbids_regular_user_from_recording_hours_for_another_employee()
    {
        var organizationId = Guid.NewGuid();
        var currentUserId = Guid.NewGuid();
        var jobs = new StubJobService(Result<JobReportSummaryResponse>.NotFound());
        var worksheets = new RecordingWorksheetRepository();
        var service = CreateService(worksheets, jobs, currentUserId, organizationId, Roles.User);

        var result = await service.UpsertAsync(
            new UpsertWorksheetRequest(
                Id: null,
                JobId: Guid.NewGuid(),
                UserId: Guid.NewGuid(),
                UserDisplayName: "Another employee",
                WorkDate: new DateOnly(2026, 8, 13),
                HoursWorked: 1m,
                SleptOnJob: false),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, result.Status);
        Assert.Equal(0, jobs.GetSingleCalls);
        Assert.Equal(0, worksheets.UpsertCalls);
    }

    [Fact]
    public async Task DeleteAsync_checks_job_access_before_deleting_a_worksheet()
    {
        var jobs = new StubJobService(Result<JobReportSummaryResponse>.NotFound());
        var worksheets = new RecordingWorksheetRepository();
        var service = CreateService(worksheets, jobs, Guid.NewGuid(), Guid.NewGuid(), Roles.User);

        var result = await service.DeleteAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(1, jobs.GetSingleCalls);
        Assert.Equal(0, worksheets.ListByJobCalls);
        Assert.Equal(0, worksheets.DeleteCalls);
    }

    [Fact]
    public async Task DeleteAsync_does_not_allow_regular_user_to_delete_a_colleagues_worksheet()
    {
        var organizationId = Guid.NewGuid();
        var currentUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var foreignWorksheet = CreateWorksheet(Guid.NewGuid(), jobId, Guid.NewGuid(), organizationId);
        var jobs = new StubJobService(Result<JobReportSummaryResponse>.Success(CreateJob(jobId, organizationId, currentUserId)));
        var worksheets = new RecordingWorksheetRepository([foreignWorksheet]);
        var service = CreateService(worksheets, jobs, currentUserId, organizationId, Roles.User);

        var result = await service.DeleteAsync(foreignWorksheet.Id, jobId, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(1, jobs.GetSingleCalls);
        Assert.Equal(1, worksheets.ListByJobCalls);
        Assert.Equal(0, worksheets.DeleteCalls);
    }

    [Fact]
    public async Task DeleteAsync_allows_regular_user_to_delete_their_own_worksheet()
    {
        var organizationId = Guid.NewGuid();
        var currentUserId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var ownWorksheet = CreateWorksheet(Guid.NewGuid(), jobId, currentUserId, organizationId);
        var jobs = new StubJobService(Result<JobReportSummaryResponse>.Success(CreateJob(jobId, organizationId, currentUserId)));
        var worksheets = new RecordingWorksheetRepository([ownWorksheet]);
        var service = CreateService(worksheets, jobs, currentUserId, organizationId, Roles.User);

        var result = await service.DeleteAsync(ownWorksheet.Id, jobId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, jobs.GetSingleCalls);
        Assert.Equal(1, worksheets.ListByJobCalls);
        Assert.Equal(1, worksheets.DeleteCalls);
    }

    private static WorksheetService CreateService(
        IWorksheetRepository worksheets,
        IJobService jobs,
        Guid userId,
        Guid organizationId,
        string role) =>
        new(
            worksheets,
            jobs,
            new InlineValidator<UpsertWorksheetRequest>(),
            new StubCurrentUserContext(userId, organizationId, role),
            null!,
            NullLogger<WorksheetService>.Instance);

    private static JobReportSummaryResponse CreateJob(Guid jobId, Guid organizationId, Guid assignedUserId) =>
        new(
            Id: jobId,
            OrganizationId: organizationId,
            OrganizationName: "Test organization",
            OrganizationCvr: "12345678",
            ReportNumber: "0001",
            Status: JobStatus.Draft,
            CustomerId: null,
            CustomerSnapshot: new CustomerSnapshotResponse(null, null, null, null, null),
            DestinationAddress: null,
            DestinationZipCode: null,
            DestinationCity: null,
            JobType: JobType.KLS.ToString(),
            Work: new JobReportSummaryWorkResponse(null, [], [], null),
            Observations: new JobReportSummaryObservationResponse(null, null, null),
            ControlInstallationTypes: [],
            Links: [],
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow,
            SubmittedAt: null,
            AssignedUsers: [new AssignedUserResponse(assignedUserId, "Current employee")],
            Worksheets: [],
            TotalHours: null,
            TotalOutlay: null,
            SoftDeleted: false,
            RejectionNote: null);

    private static WorksheetResponse CreateWorksheet(
        Guid worksheetId,
        Guid jobId,
        Guid userId,
        Guid organizationId) =>
        new(
            worksheetId,
            organizationId,
            jobId,
            userId,
            "Employee",
            new DateOnly(2026, 8, 13),
            1m,
            false,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    private sealed record StubCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

    private sealed class RecordingWorksheetRepository(
        IReadOnlyList<WorksheetResponse>? worksheets = null) : IWorksheetRepository
    {
        private readonly IReadOnlyList<WorksheetResponse> items = worksheets ?? [];

        public int UpsertCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public int ListByJobCalls { get; private set; }

        public Task<WorksheetResponse> UpsertAsync(UpsertWorksheetRequest request, CancellationToken cancellationToken)
        {
            UpsertCalls++;
            throw new InvalidOperationException("Forbidden upsert must not reach the repository.");
        }

        public Task DeleteAsync(Guid worksheetId, Guid jobId, CancellationToken cancellationToken)
        {
            DeleteCalls++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WorksheetResponse>> ListByJobAsync(Guid jobId, CancellationToken cancellationToken)
        {
            ListByJobCalls++;
            return Task.FromResult<IReadOnlyList<WorksheetResponse>>(
                items.Where(worksheet => worksheet.JobId == jobId).ToArray());
        }

        public Task<IReadOnlyList<WorksheetUserGroupResponse>> GetGroupedByJobAsync(Guid jobId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, decimal?>> GetTotalHoursByJobAsync(IEnumerable<Guid> jobIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<decimal> GetHoursForUserDayAsync(Guid organizationId, Guid userId, DateOnly workDate, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MyWorksheetEntryResponse>> GetWorksheetsForUserAsync(Guid userId, Guid organizationId, DateOnly monthStart, DateOnly monthEnd, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MyWorksheetEntryResponse>> GetAllWorksheetsAsync(Guid organizationId, DateOnly monthStart, DateOnly monthEnd, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StubJobService(Result<JobReportSummaryResponse> getSingleResult) : IJobService
    {
        public int GetSingleCalls { get; private set; }

        public Task<Result<JobReportSummaryResponse>> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobListResponse>> ListAsync(List<JobStatus>? statuses, string? reportNumber, string? customerName, string? customerEmail, string? customerAddress, string? search, string? sortBy, string? sortDirection, int? limit, int? offset, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<JobListItemResponse>>> GetMyAssignedJobsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> GetSingleJobAsync(Guid id, CancellationToken cancellationToken)
        {
            GetSingleCalls++;
            return Task.FromResult(getSingleResult);
        }

        public Task<Result<IReadOnlyList<JobHistoryResponse>>> GetHistoryAsync(Guid id, int? limit, int? offset, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> ChangeStatusAsync(Guid id, ChangeJobStatusRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> AssignAsync(Guid jobId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> CreateLinksAsync(Guid reportId, CreateJobLinkRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> DeleteLinksAsync(Guid reportId, DeleteJobLinksRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobDeleteErrorResponse>> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> RestoreDeletionAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> MarkJobAsSeenAsync(Guid id, string? viewType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task InvalidateJobDetailCacheAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
