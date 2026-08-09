namespace Workslip.Application.Jobs;

public sealed record JobAssignmentUserScope(
    Guid Id,
    Guid FilialId,
    string Role);

public interface IJobAssignmentScopeRepository
{
    Task<Guid?> GetDefaultFilialIdAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<Guid?> GetJobFilialIdAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobAssignmentUserScope>> GetUserScopesAsync(
        Guid organizationId,
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken);
}
