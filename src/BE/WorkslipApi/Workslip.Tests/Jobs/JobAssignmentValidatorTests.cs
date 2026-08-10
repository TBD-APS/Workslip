using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Domain;

namespace Workslip.Tests.Jobs;

public sealed class JobAssignmentValidatorTests
{
    [Fact]
    public async Task Existing_job_allows_employee_from_same_filial_and_audience()
    {
        var organizationId = Guid.NewGuid();
        var filialId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var validator = CreateValidator(
            organizationId,
            filialId,
            new JobAssignmentUserScope(employeeId, filialId, Roles.User, UserKinds.Member));

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
            new JobAssignmentUserScope(employeeId, Guid.NewGuid(), Roles.User, UserKinds.Member));

        var result = await validator.ValidateForExistingJobAsync(
            Guid.NewGuid(),
            [employeeId],
            CancellationToken.None);

        Assert.Equal(JobAssignmentValidationStatus.InvalidAssignee, result.Status);
    }

    [Fact]
    public async Task Existing_job_allows_another_admin_from_same_filial_and_audience()
    {
        var organizationId = Guid.NewGuid();
        var filialId = Guid.NewGuid();
        var otherAdminId = Guid.NewGuid();
        var validator = CreateValidator(
            organizationId,
            filialId,
            new JobAssignmentUserScope(otherAdminId, filialId, Roles.Admin, UserKinds.Member));

        var result = await validator.ValidateForExistingJobAsync(
            Guid.NewGuid(),
            [otherAdminId],
            CancellationToken.None);

        Assert.Equal(JobAssignmentValidationStatus.Valid, result.Status);
    }

    [Fact]
    public async Task Existing_job_rejects_admin_from_another_filial()
    {
        var organizationId = Guid.NewGuid();
        var jobFilialId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var validator = CreateValidator(
            organizationId,
            jobFilialId,
            new JobAssignmentUserScope(adminId, Guid.NewGuid(), Roles.Admin, UserKinds.Member));

        var result = await validator.ValidateForExistingJobAsync(
            Guid.NewGuid(),
            [adminId],
            CancellationToken.None);

        Assert.Equal(JobAssignmentValidationStatus.InvalidAssignee, result.Status);
    }

    [Fact]
    public async Task Member_admin_rejects_internal_test_target_in_same_filial()
    {
        var organizationId = Guid.NewGuid();
        var filialId = Guid.NewGuid();
        var testUserId = Guid.NewGuid();
        var validator = CreateValidator(
            organizationId,
            filialId,
            Roles.Admin,
            UserKinds.Member,
            new JobAssignmentUserScope(testUserId, filialId, Roles.User, UserKinds.InternalTest));

        var result = await validator.ValidateForExistingJobAsync(
            Guid.NewGuid(),
            [testUserId],
            CancellationToken.None);

        Assert.Equal(JobAssignmentValidationStatus.InvalidAssignee, result.Status);
    }

    [Fact]
    public async Task Internal_test_admin_allows_internal_test_target_in_same_filial()
    {
        var organizationId = Guid.NewGuid();
        var filialId = Guid.NewGuid();
        var testUserId = Guid.NewGuid();
        var validator = CreateValidator(
            organizationId,
            filialId,
            Roles.Admin,
            UserKinds.InternalTest,
            new JobAssignmentUserScope(testUserId, filialId, Roles.User, UserKinds.InternalTest));

        var result = await validator.ValidateForExistingJobAsync(
            Guid.NewGuid(),
            [testUserId],
            CancellationToken.None);

        Assert.Equal(JobAssignmentValidationStatus.Valid, result.Status);
    }

    [Fact]
    public async Task Superadmin_can_validate_internal_test_target_without_joining_test_audience()
    {
        var organizationId = Guid.NewGuid();
        var filialId = Guid.NewGuid();
        var testAdminId = Guid.NewGuid();
        var validator = CreateValidator(
            organizationId,
            filialId,
            Roles.Superadmin,
            UserKinds.Member,
            new JobAssignmentUserScope(testAdminId, filialId, Roles.Admin, UserKinds.InternalTest));

        var result = await validator.ValidateForExistingJobAsync(
            Guid.NewGuid(),
            [testAdminId],
            CancellationToken.None);

        Assert.Equal(JobAssignmentValidationStatus.Valid, result.Status);
    }

    [Theory]
    [InlineData(Roles.Auditor)]
    [InlineData(Roles.Superadmin)]
    public async Task Existing_job_rejects_non_assignment_target_roles(string role)
    {
        var organizationId = Guid.NewGuid();
        var filialId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var validator = CreateValidator(
            organizationId,
            filialId,
            new JobAssignmentUserScope(userId, filialId, role, UserKinds.Member));

        var result = await validator.ValidateForExistingJobAsync(
            Guid.NewGuid(),
            [userId],
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
        var repository = new StubScopeRepository(
            defaultFilialId: Guid.NewGuid(),
            jobFilialId: null,
            actorUserKind: UserKinds.Member,
            []);
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
    public async Task Default_filial_validation_allows_admin_assignment_in_same_audience()
    {
        var organizationId = Guid.NewGuid();
        var filialId = Guid.NewGuid();
        var adminId = Guid.NewGuid();
        var validator = CreateValidator(
            organizationId,
            filialId,
            new JobAssignmentUserScope(adminId, filialId, Roles.Admin, UserKinds.Member));

        var result = await validator.ValidateForDefaultFilialAsync(
            [adminId],
            CancellationToken.None);

        Assert.Equal(JobAssignmentValidationStatus.Valid, result.Status);
    }

    private static JobAssignmentValidator CreateValidator(
        Guid organizationId,
        Guid filialId,
        params JobAssignmentUserScope[] users) =>
        CreateValidator(organizationId, filialId, Roles.Admin, UserKinds.Member, users);

    private static JobAssignmentValidator CreateValidator(
        Guid organizationId,
        Guid filialId,
        string actorRole,
        string actorUserKind,
        params JobAssignmentUserScope[] users)
    {
        return new JobAssignmentValidator(
            new StubScopeRepository(filialId, filialId, actorUserKind, users),
            new TestCurrentUserContext(Guid.NewGuid(), organizationId, actorRole));
    }

    private sealed record TestCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

    private sealed class StubScopeRepository(
        Guid? defaultFilialId,
        Guid? jobFilialId,
        string? actorUserKind,
        IReadOnlyList<JobAssignmentUserScope> users) : IJobAssignmentScopeRepository
    {
        public Task<Guid?> GetDefaultFilialIdAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(defaultFilialId);

        public Task<Guid?> GetJobFilialIdAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken) =>
            Task.FromResult(jobFilialId);

        public Task<string?> GetUserKindAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(actorUserKind);

        public Task<IReadOnlyList<JobAssignmentUserScope>> GetUserScopesAsync(
            Guid organizationId,
            IReadOnlyList<Guid> userIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<JobAssignmentUserScope>>(
                users.Where(user => userIds.Contains(user.Id)).ToArray());
    }
}
