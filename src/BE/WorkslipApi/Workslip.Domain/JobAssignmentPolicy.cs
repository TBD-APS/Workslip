namespace Workslip.Domain;

public static class JobAssignmentPolicy
{
    public static bool CanReceiveAssignment(string? role) =>
        string.Equals(role, Roles.User, StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, Roles.Admin, StringComparison.OrdinalIgnoreCase);
}
