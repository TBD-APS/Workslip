using Workslip.Domain;

namespace Workslip.Application.Jobs;

public sealed record JobQuery(Guid OrganizationId, JobStatus? Status, int Limit, int Offset, string? ReportNumberSearch = null, string? CustomerNameSearch = null, string? CustomerEmailSearch = null, string? CustomerAddressSearch = null);

public interface IJobRepository
{
    Task<JobReportResponse> CreateAsync(Guid organizationId, CreateJobRequest request, IReadOnlyList<Guid> assignedUserIds, Guid? actorId, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobListItemResponse>> ListAsync(JobQuery query, CancellationToken cancellationToken);
    Task<JobReportResponse?> GetAsync(Guid id, Guid organizationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<JobEventResponse>?> GetEventsAsync(Guid id, Guid organizationId, int limit, int offset, CancellationToken cancellationToken);
    Task<JobReportResponse?> UpdateAsync(Guid id, Guid organizationId, UpdateJobRequest request, CancellationToken cancellationToken);
    Task<JobReportResponse?> TransitionAsync(Guid id, Guid organizationId, JobStatus nextStatus, Guid? actorId, CancellationToken cancellationToken);
    Task<JobReportResponse?> DeleteAsync(Guid id, Guid organizationId, CancellationToken cancellationToken);
    Task<JobReportResponse?> RestoreDeletionAsync(Guid id, Guid organizationId, CancellationToken cancellationToken);
    Task<int> PurgeDeletionScheduledBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
    Task<JobReportResponse?> AssignAsync(Guid jobId, Guid organizationId, IReadOnlyList<Guid> userIds, Guid? actorId, CancellationToken cancellationToken);
}
