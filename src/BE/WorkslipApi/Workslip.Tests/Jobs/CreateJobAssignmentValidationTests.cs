using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Jobs.Validators;
using Workslip.Application.Worksheets;
using Workslip.Domain;

namespace Workslip.Tests.Jobs;

public sealed class CreateJobAssignmentValidationTests
{
    [Fact]
    public async Task Validator_rejects_timesheet_for_user_not_assigned_to_job()
    {
        var organizationId = Guid.NewGuid();
        var nielsId = Guid.NewGuid();
        var arneId = Guid.NewGuid();
        var validator = new CreateJobRequestValidator(
            new AllowAssignmentValidator(),
            new EmptyWorksheetRepository(),
            new TestCurrentUserContext(Guid.NewGuid(), organizationId, Roles.Admin));

        var request = new CreateJobRequest(
            JobType: JobType.Diverse.ToString(),
            Timesheets: [new CreateTimesheetRequest("2026-08-09", arneId.ToString(), 8m, false)],
            AssignedUserIds: [nielsId]);

        var result = await validator.ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.PropertyName == nameof(CreateJobRequest.Timesheets)
            && error.ErrorMessage.Contains("tildelt sagen", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Validator_allows_timesheet_for_assigned_user()
    {
        var organizationId = Guid.NewGuid();
        var nielsId = Guid.NewGuid();
        var validator = new CreateJobRequestValidator(
            new AllowAssignmentValidator(),
            new EmptyWorksheetRepository(),
            new TestCurrentUserContext(Guid.NewGuid(), organizationId, Roles.Admin));

        var request = new CreateJobRequest(
            JobType: JobType.Diverse.ToString(),
            Timesheets: [new CreateTimesheetRequest("2026-08-09", nielsId.ToString(), 8m, false)],
            AssignedUserIds: [nielsId]);

        var result = await validator.ValidateAsync(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task Validator_rejects_assignment_outside_default_filial()
    {
        var organizationId = Guid.NewGuid();
        var validator = new CreateJobRequestValidator(
            new RejectAssignmentValidator(),
            new EmptyWorksheetRepository(),
            new TestCurrentUserContext(Guid.NewGuid(), organizationId, Roles.Admin));

        var request = new CreateJobRequest(
            JobType: JobType.Diverse.ToString(),
            AssignedUserIds: [Guid.NewGuid()]);

        var result = await validator.ValidateAsync(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error =>
            error.PropertyName == nameof(CreateJobRequest.AssignedUserIds)
            && error.ErrorMessage == "Sager kan kun tildeles medarbejdere i samme filial.");
    }

    private sealed record TestCurrentUserContext(Guid? UserId, Guid? OrganizationId, string? Role) : ICurrentUserContext;

    private sealed class AllowAssignmentValidator : IJobAssignmentValidator
    {
        public Task<JobAssignmentValidationResult> ValidateForExistingJobAsync(Guid jobId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult(JobAssignmentValidationResult.Valid());

        public Task<JobAssignmentValidationResult> ValidateForDefaultFilialAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult(JobAssignmentValidationResult.Valid());
    }

    private sealed class RejectAssignmentValidator : IJobAssignmentValidator
    {
        public Task<JobAssignmentValidationResult> ValidateForExistingJobAsync(Guid jobId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult(JobAssignmentValidationResult.InvalidAssignee());

        public Task<JobAssignmentValidationResult> ValidateForDefaultFilialAsync(IReadOnlyList<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult(JobAssignmentValidationResult.InvalidAssignee());
    }

    private sealed class EmptyWorksheetRepository : IWorksheetRepository
    {
        public Task<decimal> GetHoursForUserDayAsync(Guid organizationId, Guid userId, DateOnly workDate, CancellationToken cancellationToken) => Task.FromResult(0m);
        public Task<WorksheetResponse> UpsertAsync(UpsertWorksheetRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(Guid worksheetId, Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorksheetResponse>> ListByJobAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorksheetUserGroupResponse>> GetGroupedByJobAsync(Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, decimal?>> GetTotalHoursByJobAsync(IEnumerable<Guid> jobIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MyWorksheetEntryResponse>> GetWorksheetsForUserAsync(Guid userId, Guid organizationId, DateOnly monthStart, DateOnly monthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<MyWorksheetEntryResponse>> GetAllWorksheetsAsync(Guid organizationId, DateOnly monthStart, DateOnly monthEnd, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
