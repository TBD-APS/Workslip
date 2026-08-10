using Workslip.Domain;

namespace Workslip.Tests.Users;

public sealed class UserVisibilityPolicyTests
{
    [Theory]
    [InlineData(Roles.User)]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Auditor)]
    public void Member_actor_can_access_member_non_superadmin(string targetRole)
    {
        Assert.True(UserVisibilityPolicy.CanAccess(
            Roles.Admin,
            UserKinds.Member,
            targetRole,
            UserKinds.Member));
    }

    [Fact]
    public void Member_actor_cannot_access_internal_test_user()
    {
        Assert.False(UserVisibilityPolicy.CanAccess(
            Roles.Admin,
            UserKinds.Member,
            Roles.User,
            UserKinds.InternalTest));
    }

    [Fact]
    public void Internal_test_actor_can_access_internal_test_user()
    {
        Assert.True(UserVisibilityPolicy.CanAccess(
            Roles.Admin,
            UserKinds.InternalTest,
            Roles.User,
            UserKinds.InternalTest));
    }

    [Fact]
    public void Internal_test_actor_cannot_access_member_user()
    {
        Assert.False(UserVisibilityPolicy.CanAccess(
            Roles.Admin,
            UserKinds.InternalTest,
            Roles.User,
            UserKinds.Member));
    }

    [Fact]
    public void Non_superadmin_never_accesses_superadmin_through_user_kind()
    {
        Assert.False(UserVisibilityPolicy.CanAccess(
            Roles.Admin,
            UserKinds.Member,
            Roles.Superadmin,
            UserKinds.Member));

        Assert.False(UserVisibilityPolicy.CanAccess(
            Roles.Admin,
            UserKinds.InternalTest,
            Roles.Superadmin,
            UserKinds.InternalTest));
    }

    [Theory]
    [InlineData(UserKinds.Member)]
    [InlineData(UserKinds.InternalTest)]
    public void Superadmin_can_access_both_user_kinds(string targetUserKind)
    {
        Assert.True(UserVisibilityPolicy.CanAccess(
            Roles.Superadmin,
            UserKinds.Member,
            Roles.User,
            targetUserKind));
    }

    [Theory]
    [InlineData(null, UserKinds.Member)]
    [InlineData("Unknown", UserKinds.Member)]
    [InlineData(UserKinds.Member, null)]
    [InlineData(UserKinds.Member, "Unknown")]
    public void Non_superadmin_fails_closed_for_unknown_user_kind(string? actorUserKind, string? targetUserKind)
    {
        Assert.False(UserVisibilityPolicy.CanAccess(
            Roles.Admin,
            actorUserKind,
            Roles.User,
            targetUserKind));
    }
}
