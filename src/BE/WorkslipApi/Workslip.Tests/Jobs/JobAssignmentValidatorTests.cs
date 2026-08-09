using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Domain;

namespace Workslip.Tests.Jobs;

public sealed class JobAssignmentValidatorTests
{
    [Fact]
    public async Task Existing_job_allows_employee_from_same_filial()
    {
        var organizationId = Guid.NewGuid();
        var filialId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var validator = CreateValidator(
            organizationId,
            filialId,
            new JobAssignmentUserScope(employeeId, filialId, Roles.User));

        var result = await validator.ValidateForExistingJobAsync(
            Guid.NewGuid(),
            [employeeId],
            CancellationToken.None);

        Assert.Equal(JobAssignmentValidationStatus.Valid, result.Status);
    }

    [Fact]
    public async Task Existing_job_rejects_employee_from_another_filial()
    {
        var organizationId = Guid.NewGuid();
        var jobFilialId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var validator = CreateValidator(
            organizationId,
            jobFilialId,
            new JobAssignmentUserScope(employeeId, Guid.NewGuid(), Roles.User));

        var result = await validator.ValidateForExistingJobAsync(
            Guid.NewGuid(),
            [employeeId],
            CancellationToken.None);

        Assert.Equal(JobAssignmentValidationStatus.InvalidAssignee, result.Status);
    }

    [Fact]
    public async Task Existing_job_allows_admin_to_assign_self_in_same_filial()
    {
        var organizationId = Guid.NewGuid();
        var filialId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var validator = CreateValidator(
            organizationId,
            filialId,
            adminId,
            new JobAssignmentUserScope(adminId, filialId, Roles.Admin));

        var result = await validator.ValidateForExistingJobAsync(
            Guid.NewGuid(),
            [adminId],
            CancellationToken.None);

        Assert.Equal(JobAssignmentValidationStatus.Valid, result.Status);
    }

    [Fact]
    public async Task Existing_job_rejects_another_admin_even_in_same_filial()
    {
        var organizationId = Guid.NewGuid();
        var filialId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var otherAdminId = Guid.NewGuid();
        var validator = CreateValidator(
            organizationId,
            filialId,
            actorId,
            new JobAssignmentUserScope(otherAdminId, filialId, Roles.Admin));

        var result = await validator.ValidateForExistingJobAsync(
            Guid.NewGuid(),
            [otherAdminId],
            CancellationToken.None);

        Assert.Equal(JobAssignmentValidationStatus.InvalidAssignee, result.Status);
    }

    [Fact]
    public async Task Existing_job_rejects_admin_self_from_another_filial()
    {
        var organizationId = Guid.NewGuid();
        var jobFilialId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var validator = CreateValidator(
            organizationId,
            jobFilialId,
            adminId,
            new JobAssignmentUserScope(adminId, Guid.NewGuid(), Roles.Admin));

        var result = await validator.ValidateForExistingJobAsync(
            Guid.NewGuid(),
            [adminId],
            CancellationToken.None);

        Assert.Equal(JobAssignmentValidationStatus.InvalidAssignee, result.Status);
    }

    [Fact]
    public async Task Existing_job_rejects_user_not_found_in_effective_organization()
    {
        var organizationId = Guid.NewGuid();
        var validator = CreateValidator(organizationId, Guid.NewGuid());

        var result = await validator.ValidateForExistingJobAsync(
            Guid.NewGuid(),
            [Guid.NewGuid()],
            CancellationToken.None);

        Assert.Equal(JobAssignmentValidationStatus.InvalidAssignee, result.Status);
    }

    [Fact]
    public async Task Existing_job_returns_not_found_when_job_is_outside_effective_organization()
    {
        var organizationId = Guid.NewGuid();
        var repository = new StubScopeRepository(defaultFilialId: Guid.NewGuid(), jobFilialId: null, []);
        var validator = new JobAssignmentValidator(
            repository,
            new TestCurrentUserContext(Guid.NewGuid(), organizationId, Roles.Admin));

        var result = await validator.ValidateForExistingJobAsync(
            Guid.NewGuid(),
            [Guid.NewGuid()],
            CancellationToken.None);

        Assert.Equal(JobAssignmentValidationStatus.JobNotFound, result.Status);
    }

    [Fact]
    public async Task Default_filial_validation_uses_same_rule_for_initial_assignment()
    {
        var organizationId = Guid.NewGuid();
        var filialId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var validator = CreateValidator(
            organizationId,
            filialId,
            adminId,
            new JobAssignmentUserScope(adminId, filialId, Roles.Admin));

        var result = await validator.ValidateForDefaultFilialAsync(
            [adminId],
            CancellationToken.None);

        Assert.Equal(JobAssignmentValidationStatus.Valid, result.Status);
    }

    private static JobAssignmentValidator CreateValidator(
        Guid organizationId,
        Guid filialId,
        params JobAssignmentUserScope[] users) =>
        CreateValidator(organizationId, filialId, Guid.NewGuid(), users);

    private static JobAssignmentValidator CreateValidator(
        Guid organizationId,
        Guid filialId,
        Guid actorId,
        params JobAssignmentUserScope[] users)
    {
        return new JobAssignmentValidator(
            new StubScopeRepository(filialId, filialId, users),
            new TestCurrentUserContext(actorId, organizationId, Roles.Admin));
    }

    private sealed record TestCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

    private sealed class StubScopeRepository(
        Guid? defaultFilialId,
        Guid? jobFilialId,
        IReadOnlyList<JobAssignmentUserScope> users) : IJobAssignmentScopeRepository
    {
        public Task<Guid?> GetDefaultFilialIdAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(defaultFilialId);

        public Task<Guid?> GetJobFilialIdAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken) =>
            Task.FromResult(jobFilialId);

        public Task<IReadOnlyList<JobAssignmentUserScope>> GetUserScopesAsync(
            Guid organizationId,
            IReadOnlyList<Guid> userIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobAssignmentUserScope>>(
                users.Where(user => userIds.Contains(user.Id)).ToArray());
    }
}
