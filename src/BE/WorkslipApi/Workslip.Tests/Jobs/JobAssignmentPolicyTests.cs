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
}
