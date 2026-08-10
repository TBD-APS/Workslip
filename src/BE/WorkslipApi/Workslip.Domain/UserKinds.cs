namespace Workslip.Domain;

public static class UserKinds
{
    public const string Member = "Member";
    public const string InternalTest = "InternalTest";

    public static bool IsKnown(string? userKind) =>
        string.Equals(userKind, Member, StringComparison.OrdinalIgnoreCase)
        || string.Equals(userKind, InternalTest, StringComparison.OrdinalIgnoreCase);

    public static string? Normalize(string? userKind)
    {
        if (string.Equals(userKind, Member, StringComparison.OrdinalIgnoreCase))
            return Member;

        if (string.Equals(userKind, InternalTest, StringComparison.OrdinalIgnoreCase))
            return InternalTest;

        return null;
    }
}

public static class UserVisibilityPolicy
{
    public static bool CanAccess(
        string? actorRole,
        string? actorUserKind,
        string? targetRole,
        string? targetUserKind)
    {
        if (string.Equals(actorRole, Roles.Superadmin, StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(targetRole, Roles.Superadmin, StringComparison.OrdinalIgnoreCase))
            return false;

        var normalizedActorKind = UserKinds.Normalize(actorUserKind);
        var normalizedTargetKind = UserKinds.Normalize(targetUserKind);
        return normalizedActorKind is not null
            && normalizedTargetKind is not null
            && string.Equals(normalizedActorKind, normalizedTargetKind, StringComparison.Ordinal);
    }
}
