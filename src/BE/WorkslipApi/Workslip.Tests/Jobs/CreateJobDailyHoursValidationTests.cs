using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Jobs.Validators;
using Workslip.Application.Worksheets;
using Workslip.Domain;

namespace Workslip.Tests.Jobs;

public sealed class CreateJobDailyHoursValidationTests
{
    private static readonly Guid OrganizationId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Validator_rejects_hours_above_24_across_existing_jobs()
    {
        var validator = CreateValidator(existingHours: 20m);
        var request = CreateRequest(new CreateTimesheetRequest("2026-08-09", UserId.ToString(), 4.25m, false));

        var result = await validator.ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.PropertyName == nameof(CreateJobRequest.Timesheets)
            && error.ErrorMessage == WorksheetHourRules.DailyLimitMessage);
    }

    [Fact]
    public async Task Validator_sums_multiple_new_entries_for_same_user_and_day()
    {
        var validator = CreateValidator(existingHours: 0m);
        var request = CreateRequest(
            new CreateTimesheetRequest("2026-08-09", UserId.ToString(), 12m, false),
            new CreateTimesheetRequest("2026-08-09", UserId.ToString(), 12.25m, false));

        var result = await validator.ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(CreateJobRequest.Timesheets));
    }

    [Fact]
    public async Task Validator_allows_exactly_24_hours_for_assigned_user()
    {
        var validator = CreateValidator(existingHours: 20m);
        var request = CreateRequest(new CreateTimesheetRequest("2026-08-09", UserId.ToString(), 4m, false));

        var result = await validator.ValidateAsync(request);

        Assert.True(result.IsValid);
    }

    private static CreateJobRequestValidator CreateValidator(decimal existingHours) =>
        new(
            new AllowAssignmentValidator(),
            new StubWorksheetRepository(existingHours),
            new StubCurrentUserContext(OrganizationId));

    private static CreateJobRequest CreateRequest(params CreateTimesheetRequest[] timesheets) =>
        new(JobType: JobType.Diverse.ToString(), Timesheets: timesheets, AssignedUserIds: [UserId]);

    private sealed class StubCurrentUserContext(Guid organizationId) : ICurrentUserContext
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid? OrganizationId => organizationId;
        public string? Role => Roles.Admin;
    }

    private sealed class AllowAssignmentValidator : IJobAssignmentValidator
    {
        public Task<JobAssignmentValidationResult> ValidateForExistingJobAsync(Guid jobId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult(JobAssignmentValidationResult.Valid());

        public Task<JobAssignmentValidationResult> ValidateForDefaultFilialAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult(JobAssignmentValidationResult.Valid());
    }

    private sealed class StubWorksheetRepository(decimal existingHours) : IWorksheetRepository
    {
        public Task<decimal> GetHoursForUserDayAsync(Guid organizationId, Guid userId, DateOnly workDate, CancellationToken cancellationToken) =>
            Task.FromResult(existingHours);

        public Task<WorksheetResponse> UpsertAsync(UpsertWorksheetRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(Guid worksheetId, Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorksheetResponse>> ListByJobAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorksheetUserGroupResponse>> GetGroupedByJobAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, decimal?>> GetTotalHoursByJobAsync(IEnumerable<Guid> jobIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MyWorksheetEntryResponse>> GetWorksheetsForUserAsync(Guid userId, Guid organizationId, DateOnly monthStart, DateOnly monthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MyWorksheetEntryResponse>> GetAllWorksheetsAsync(Guid organizationId, DateOnly monthStart, DateOnly monthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
