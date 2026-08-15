using Workslip.Application.Jobs;
using Workslip.Domain;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

internal sealed class BillingAwareJobRepository(
    IJobRepository inner,
    SqlDbContext dbContext) : IJobRepository
{
    public Task<JobReportResponse> CreateAsync(
        Guid organizationId,
        CreateJobRequest request,
        IReadOnlyList<Guid> assignedUserIds,
        Guid? actorId,
        CancellationToken cancellationToken) =>
        inner.CreateAsync(organizationId, request, assignedUserIds, actorId, cancellationToken);

    public Task<JobListResponse> ListAsync(JobQuery query, CancellationToken cancellationToken) =>
        inner.ListAsync(query, cancellationToken);

    public Task<JobReportResponse?> GetSingleJobAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken) =>
        inner.GetSingleJobAsync(id, organizationId, cancellationToken);

    public Task<IReadOnlyList<JobHistoryResponse>?> GetEventsAsync(
        Guid id,
        Guid organizationId,
        int limit,
        int offset,
        CancellationToken cancellationToken) =>
        inner.GetEventsAsync(id, organizationId, limit, offset, cancellationToken);

    public Task<JobReportResponse?> UpdateAsync(
        Guid id,
        Guid organizationId,
        UpdateJobRequest request,
        CancellationToken cancellationToken) =>
        inner.UpdateAsync(id, organizationId, request, cancellationToken);

    public async Task<JobTransitionResult?> TransitionAsync(
        Guid id,
        Guid organizationId,
        JobStatus nextStatus,
        Guid? actorId,
        string? rejectionNote,
        CancellationToken cancellationToken)
    {
        var result = await inner.TransitionAsync(
            id,
            organizationId,
            nextStatus,
            actorId,
            rejectionNote,
            cancellationToken);

        if (result is not null && nextStatus == JobStatus.Approved)
        {
            await WorksheetBillingSnapshots.CaptureAsync(
                dbContext,
                [(id, organizationId)],
                cancellationToken);
        }

        return result;
    }

    public Task<JobDeleteRepositoryResult> DeleteAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken) =>
        inner.DeleteAsync(id, organizationId, cancellationToken);

    public Task<JobReportResponse?> RestoreDeletionAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken) =>
        inner.RestoreDeletionAsync(id, organizationId, cancellationToken);

    public Task<int> PurgeDeletionScheduledBeforeAsync(
        DateTimeOffset cutoff,
        CancellationToken cancellationToken) =>
        inner.PurgeDeletionScheduledBeforeAsync(cutoff, cancellationToken);
}
