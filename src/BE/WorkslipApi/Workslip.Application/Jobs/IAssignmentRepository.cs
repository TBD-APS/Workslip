namespace Workslip.Application.Jobs;

public interface IAssignmentRepository
{
    Task AssignAsync(Guid jobId, Guid organizationId, IReadOnlyList<Guid> userIds, Guid? actorId, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobListItemResponse>> GetMyAssignedJobsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<AssignedUserResponse>>> GetAssignedUsersByReportAsync(Guid organizationId, IEnumerable<Guid> reportIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<AssignedUserResponse>> GetAssignedUsersByIdsAsync(Guid organizationId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken);
    Task ReplaceAssignedUsersAsync(Guid organizationId, Guid reportId, IReadOnlyList<Guid> userIds, Guid? actorId, DateTimeOffset now, CancellationToken cancellationToken);
}
