namespace Workslip.Application.Jobs;

public interface IJobViewRepository
{
    Task MarkAsViewedAsync(Guid jobId, Guid userId, string viewType, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> GetViewedJobIdsAsync(Guid userId, IReadOnlyList<Guid> jobIds, IReadOnlyList<string> viewTypes, CancellationToken cancellationToken);
}
