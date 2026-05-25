using Workslip.Domain;

namespace Workslip.Application.Jobs;

public sealed record JobQuery(Guid? OrganizationId, JobStatus? Status, int Limit, int Offset);

public interface IJobRepository
{
    Task<JobReportResponse> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobListItemResponse>> ListAsync(JobQuery query, CancellationToken cancellationToken);
    Task<JobReportResponse?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobEventResponse>?> GetEventsAsync(Guid id, int limit, int offset, CancellationToken cancellationToken);
    Task<JobReportResponse?> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken cancellationToken);
    Task<JobReportResponse?> TransitionAsync(Guid id, JobStatus nextStatus, Guid? actorId, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<JobReportResponse?> RestoreDeletionAsync(Guid id, CancellationToken cancellationToken);
    Task<int> PurgeDeletionScheduledBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
    Task<JobReportResponse?> AssignAsync(Guid jobId, Guid? userId, Guid? actorId, CancellationToken cancellationToken);
}
