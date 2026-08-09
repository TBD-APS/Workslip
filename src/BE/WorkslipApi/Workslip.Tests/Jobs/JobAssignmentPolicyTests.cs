using Workslip.Domain;

namespace Workslip.Tests.Jobs;

public sealed class JobAssignmentPolicyTests
{
    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Superadmin)]
    public void CanManageAssignments_allows_admin_roles(string role)
    {
        Assert.True(JobAssignmentPolicy.CanManageAssignments(role));
    }

    [Theory]
    [InlineData(Roles.User)]
    [InlineData(Roles.Auditor)]
    [InlineData(null)]
    [InlineData("")]
    public void CanManageAssignments_rejects_non_admin_roles(string? role)
    {
        Assert.False(JobAssignmentPolicy.CanManageAssignments(role));
    }

    [Fact]
    public void CanReceiveAssignment_allows_employee_role()
    {
        Assert.True(JobAssignmentPolicy.CanReceiveAssignment(Roles.User));
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Auditor)]
    [InlineData(Roles.Superadmin)]
    [InlineData(null)]
    [InlineData("")]
    public void CanReceiveAssignment_without_actor_context_rejects_non_employee_roles(string? role)
    {
        Assert.False(JobAssignmentPolicy.CanReceiveAssignment(role));
    }

    [Fact]
    public void CanReceiveAssignment_allows_admin_only_when_admin_actor_assigns_self()
    {
        var adminId = Guid.NewGuid();

        Assert.True(JobAssignmentPolicy.CanReceiveAssignment(Roles.Admin, adminId, adminId, Roles.Admin));
        Assert.False(JobAssignmentPolicy.CanReceiveAssignment(Roles.Admin, Guid.NewGuid(), adminId, Roles.Admin));
        Assert.False(JobAssignmentPolicy.CanReceiveAssignment(Roles.Admin, adminId, adminId, Roles.Superadmin));
        Assert.False(JobAssignmentPolicy.CanReceiveAssignment(Roles.Superadmin, adminId, adminId, Roles.Superadmin));
    }

    [Fact]
    public void CanReceiveAssignmentInFilial_requires_allowed_target_and_matching_filial()
    {
        var filialId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        Assert.True(JobAssignmentPolicy.CanReceiveAssignmentInFilial(Roles.User, employeeId, adminId, Roles.Admin, filialId, filialId));
        Assert.True(JobAssignmentPolicy.CanReceiveAssignmentInFilial(Roles.Admin, adminId, adminId, Roles.Admin, filialId, filialId));
        Assert.False(JobAssignmentPolicy.CanReceiveAssignmentInFilial(Roles.Admin, Guid.NewGuid(), adminId, Roles.Admin, filialId, filialId));
        Assert.False(JobAssignmentPolicy.CanReceiveAssignmentInFilial(Roles.User, employeeId, adminId, Roles.Admin, Guid.NewGuid(), filialId));
        Assert.False(JobAssignmentPolicy.CanReceiveAssignmentInFilial(Roles.User, employeeId, adminId, Roles.Admin, Guid.Empty, filialId));
    }

    [Fact]
    public void ResolveInitialAssignments_prefers_explicit_assignment_over_actor_fallback()
    {
        var actorId = Guid.NewGuid();
        var assignedId = Guid.NewGuid();

        var result = JobAssignmentPolicy.ResolveInitialAssignments([assignedId], actorId, Roles.Admin);

        Assert.Equal([assignedId], result);
    }

    [Theory]
    [InlineData(Roles.User)]
    [InlineData(Roles.Admin)]
    public void ResolveInitialAssignments_assigns_eligible_actor_by_default(string role)
    {
        var actorId = Guid.NewGuid();

        var result = JobAssignmentPolicy.ResolveInitialAssignments(null, actorId, role);

        Assert.Equal([actorId], result);
    }

    [Theory]
    [InlineData(Roles.Auditor)]
    [InlineData(Roles.Superadmin)]
    public void ResolveInitialAssignments_does_not_assign_ineligible_actor(string role)
    {
        var result = JobAssignmentPolicy.ResolveInitialAssignments(null, Guid.NewGuid(), role);

        Assert.Empty(result);
    }

    [Fact]
    public void AreTimesheetUsersAssigned_rejects_hours_for_unassigned_user()
    {
        var niels = Guid.NewGuid();
        var arne = Guid.NewGuid();

        Assert.False(JobAssignmentPolicy.AreTimesheetUsersAssigned([niels], [arne]));
        Assert.True(JobAssignmentPolicy.AreTimesheetUsersAssigned([niels], [niels]));
    }
}
