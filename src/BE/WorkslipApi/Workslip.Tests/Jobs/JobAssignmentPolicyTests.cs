using Workslip.Domain;

namespace Workslip.Tests.Jobs;

public sealed class JobAssignmentPolicyTests
{
    [Theory]
    [InlineData(Roles.User)]
    [InlineData(Roles.Admin)]
    public void CanReceiveAssignment_allows_tenant_worker_roles(string role)
    {
        Assert.True(JobAssignmentPolicy.CanReceiveAssignment(role));
    }

    [Theory]
    [InlineData(Roles.Auditor)]
    [InlineData(Roles.Superadmin)]
    [InlineData(null)]
    [InlineData("")]
    public void CanReceiveAssignment_rejects_non_worker_roles(string? role)
    {
        Assert.False(JobAssignmentPolicy.CanReceiveAssignment(role));
    }

    [Fact]
    public void ResolveInitialAssignments_prefers_explicit_assignment_over_actor_fallback()
    {
        var actorId = Guid.NewGuid();
        var assignedId = Guid.NewGuid();

        var result = JobAssignmentPolicy.ResolveInitialAssignments([assignedId], actorId, Roles.Admin);

        Assert.Equal([assignedId], result);
    }

    [Fact]
    public void ResolveInitialAssignments_preserves_legacy_actor_fallback()
    {
        var actorId = Guid.NewGuid();

        var result = JobAssignmentPolicy.ResolveInitialAssignments(null, actorId, Roles.Admin);

        Assert.Equal([actorId], result);
    }

    [Fact]
    public void ResolveInitialAssignments_does_not_assign_superadmin_actor()
    {
        var result = JobAssignmentPolicy.ResolveInitialAssignments(null, Guid.NewGuid(), Roles.Superadmin);

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
