using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Jobs.Validators;
using Workslip.Application.Users;
using Workslip.Application.Worksheets;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Tests.Jobs;

public sealed class CreateJobAssignmentValidationTests
{
    [Fact]
    public async Task Validator_rejects_timesheet_for_user_not_assigned_to_job()
    {
        var organizationId = Guid.NewGuid();
        var niels = CreateUser(organizationId, "niels@example.invalid");
        var arne = CreateUser(organizationId, "arne@example.invalid");
        var validator = new CreateJobRequestValidator(
            new StubUserRepository(niels, arne),
            new EmptyWorksheetRepository(),
            new TestCurrentUserContext(Guid.NewGuid(), organizationId, Roles.Admin));

        var request = new CreateJobRequest(
            JobType: JobType.Diverse.ToString(),
            Timesheets: [new CreateTimesheetRequest("2026-08-09", arne.Id.ToString(), 8m, false)],
            AssignedUserIds: [niels.Id]);

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
        var niels = CreateUser(organizationId, "niels@example.invalid");
        var validator = new CreateJobRequestValidator(
            new StubUserRepository(niels),
            new EmptyWorksheetRepository(),
            new TestCurrentUserContext(Guid.NewGuid(), organizationId, Roles.Admin));

        var request = new CreateJobRequest(
            JobType: JobType.Diverse.ToString(),
            Timesheets: [new CreateTimesheetRequest("2026-08-09", niels.Id.ToString(), 8m, false)],
            AssignedUserIds: [niels.Id]);

        var result = await validator.ValidateAsync(request);

        Assert.True(result.IsValid);
    }

    private static UserDataRow CreateUser(Guid organizationId, string email) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        Email = email,
        DisplayName = email,
        EntraId = $"entra-{Guid.NewGuid():N}",
        EntraEmail = email,
        Role = Roles.User,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private sealed record TestCurrentUserContext(Guid? UserId, Guid? OrganizationId, string? Role) : ICurrentUserContext;

    private sealed class StubUserRepository(params UserDataRow[] users) : IUserRepository
    {
        private readonly IReadOnlyDictionary<Guid, UserDataRow> _users = users.ToDictionary(user => user.Id);

        public Task<UserDataRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(_users.GetValueOrDefault(id));

        public Task<UserDataRow?> GetAuthenticatedActorAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UserDataRow?> GetByEmailAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UserDataRow?> GetByExternalIdentityAsync(string? entraId, IReadOnlyCollection<string> emailCandidates, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<UserDataRow>> GetByOrganizationIdAsync(Guid organizationId, int limit, int offset, string? search, string? sortBy, string? sortDirection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> GetCountByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> CreateAsync(UserDataRow user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateAsync(UserDataRow user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AssignedJobResponse>> GetAssignedJobsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<decimal?> GetTotalHoursAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, UserPeriodHours>> GetPeriodHoursAsync(Guid organizationId, DateOnly biweeklyStart, CancellationToken cancellationToken) => throw new NotSupportedException();
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
