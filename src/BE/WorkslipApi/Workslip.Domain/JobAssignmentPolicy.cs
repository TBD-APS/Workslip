namespace Workslip.Domain;

public static class JobAssignmentPolicy
{
    public static bool CanReceiveAssignment(string? role) =>
        string.Equals(role, Roles.User, StringComparison.OrdinalIgnoreCase);

    public static bool CanReceiveAssignmentInFilial(
        string? role,
        Guid userFilialId,
        Guid jobFilialId) =>
        CanReceiveAssignment(role)
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

        return actorId.HasValue && CanReceiveAssignment(actorRole)
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
