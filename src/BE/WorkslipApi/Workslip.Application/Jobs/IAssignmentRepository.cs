namespace Workslip.Application.Jobs;

public enum AddAssignedUserResult
{
    Added,
    AlreadyAssigned,
    Locked,
    NotFound
}

public interface IAssignmentRepository
{
    Task AssignAsync(Guid jobId, Guid organizationId, IReadOnlyList<Guid> userIds, Guid? actorId, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobListItemResponse>> GetMyAssignedJobsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<AssignedUserResponse>>> GetAssignedUsersByReportAsync(Guid organizationId, IEnumerable<Guid> reportIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<AssignedUserResponse>> GetAssignedUsersByIdsAsync(Guid organizationId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<AssignedUserResponse>> GetOrganizationAdminsAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AssignedUserResponse>> GetAssignableUsersForJobAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken);
    Task<AddAssignedUserResult> AddAssignedUserAsync(Guid organizationId, Guid jobId, Guid userId, Guid? actorId, CancellationToken cancellationToken);
    Task AddAssignedUsersAsync(Guid organizationId, Guid reportId, IReadOnlyList<Guid> userIds, Guid? actorId, DateTimeOffset now, CancellationToken cancellationToken);
}
