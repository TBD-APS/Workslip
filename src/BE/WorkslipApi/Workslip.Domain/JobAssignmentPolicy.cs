namespace Workslip.Domain;

public static class JobAssignmentPolicy
{
    public static bool CanManageAssignments(string? role) =>
        string.Equals(role, Roles.Admin, StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase);

    public static bool CanReceiveAssignment(string? role) =>
        string.Equals(role, Roles.User, StringComparison.OrdinalIgnoreCase);

    public static bool CanReceiveAssignment(
        string? role,
        Guid assigneeId,
        Guid? actorId) =>
        CanReceiveAssignment(role)
        || (actorId.HasValue
            && actorId.Value == assigneeId
            && string.Equals(role, Roles.Admin, StringComparison.OrdinalIgnoreCase));

    public static bool CanReceiveAssignmentInFilial(
        string? role,
        Guid assigneeId,
        Guid? actorId,
        Guid userFilialId,
        Guid jobFilialId) =>
        CanReceiveAssignment(role, assigneeId, actorId)
        && userFilialId != Guid.Empty
        && jobFilialId != Guid.Empty
        && userFilialId == jobFilialId;

    public static IReadOnlyList<Guid> ResolveInitialAssignments(
        IReadOnlyList<Guid>? requestedUserIds,
        Guid? actorId,
        string? actorRole)
    {
        if (requestedUserIds is not null)
        {
            return requestedUserIds
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToArray();
        }

        return actorId.HasValue && CanReceiveAssignment(actorRole, actorId.Value, actorId)
            ? [actorId.Value]
            : Array.Empty<Guid>();
    }

    public static bool AreTimesheetUsersAssigned(
        IReadOnlyCollection<Guid> assignedUserIds,
        IEnumerable<Guid> timesheetUserIds)
    {
        var assigned = assignedUserIds.ToHashSet();
        return timesheetUserIds.All(assigned.Contains);
    }
}
