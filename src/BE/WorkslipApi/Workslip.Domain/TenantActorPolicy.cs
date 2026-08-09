namespace Workslip.Domain;

public static class TenantActorPolicy
{
    public static Guid? ResolveTenantUserReference(Guid? actorId, string? actorRole)
    {
        if (actorId is null || actorId == Guid.Empty)
            return null;

        return string.Equals(actorRole, Roles.Superadmin, StringComparison.OrdinalIgnoreCase)
            ? null
            : actorId;
    }
}
