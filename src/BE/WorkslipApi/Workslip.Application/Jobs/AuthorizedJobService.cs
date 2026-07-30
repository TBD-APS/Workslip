using Ardalis.Result;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;
using Workslip.Domain;

namespace Workslip.Application.Jobs;

public sealed class AuthorizedJobService(
    JobService inner,
    IJobRepository jobRepository,
    ICurrentUserContext currentUser,
    ILogger<AuthorizedJobService> logger) : IJobService
{
    public Task<Result<JobReportSummaryResponse>> CreateAsync(
        CreateJobRequest request,
        CancellationToken cancellationToken) =>
        inner.CreateAsync(request, cancellationToken);

    public Task<Result<JobListResponse>> ListAsync(
        List<JobStatus>? statuses,
        string? reportNumber,
        string? customerName,
        string? customerEmail,
        string? customerAddress,
        string? search,
        string? sortBy,
        string? sortDirection,
        int? limit,
        int? offset,
        CancellationToken cancellationToken) =>
        inner.ListAsync(
            statuses,
            reportNumber,
            customerName,
            customerEmail,
            customerAddress,
            search,
            sortBy,
            sortDirection,
            limit,
            offset,
            cancellationToken);

    public Task<Result<IReadOnlyList<JobListItemResponse>>> GetMyAssignedJobsAsync(
        CancellationToken cancellationToken) =>
        inner.GetMyAssignedJobsAsync(cancellationToken);

    public Task<Result<JobReportSummaryResponse>> GetSingleJobAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        inner.GetSingleJobAsync(id, cancellationToken);

    public Task<Result<IReadOnlyList<JobHistoryResponse>>> GetHistoryAsync(
        Guid id,
        int? limit,
        int? offset,
        CancellationToken cancellationToken) =>
        inner.GetHistoryAsync(id, limit, offset, cancellationToken);

    public Task<Result<JobReportSummaryResponse>> UpdateAsync(
        Guid id,
        UpdateJobRequest request,
        CancellationToken cancellationToken) =>
        inner.UpdateAsync(id, request, cancellationToken);

    public async Task<Result<JobReportSummaryResponse>> ChangeStatusAsync(
        Guid id,
        ChangeJobStatusRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<JobReportSummaryResponse>.Unauthorized();
        }

        var job = await jobRepository.GetSingleJobAsync(id, organizationId.Value, cancellationToken);
        if (job is null)
        {
            return Result<JobReportSummaryResponse>.NotFound();
        }

        var decision = JobStatusTransitionPolicy.Evaluate(
            currentUser.Role,
            job.Status,
            request.Status);

        if (decision == JobStatusTransitionDecision.Forbidden)
        {
            logger.LogWarning(
                "Job transition forbidden. JobId: {JobId}. OrganizationId: {OrganizationId}. CurrentStatus: {CurrentStatus}. TargetStatus: {TargetStatus}. Role: {Role}.",
                id,
                organizationId.Value,
                job.Status,
                request.Status,
                currentUser.Role);

            return Result<JobReportSummaryResponse>.Forbidden();
        }

        if (decision == JobStatusTransitionDecision.Conflict)
        {
            logger.LogWarning(
                "Invalid job transition. JobId: {JobId}. OrganizationId: {OrganizationId}. CurrentStatus: {CurrentStatus}. TargetStatus: {TargetStatus}. Role: {Role}.",
                id,
                organizationId.Value,
                job.Status,
                request.Status,
                currentUser.Role);

            return Result<JobReportSummaryResponse>.Conflict("invalid_job_status_transition");
        }

        return await inner.ChangeStatusAsync(id, request, cancellationToken);
    }

    public Task<Result<JobReportSummaryResponse>> AssignAsync(
        Guid jobId,
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken) =>
        inner.AssignAsync(jobId, userIds, cancellationToken);

    public Task<Result<JobReportSummaryResponse>> CreateLinksAsync(
        Guid reportId,
        CreateJobLinkRequest request,
        CancellationToken cancellationToken) =>
        inner.CreateLinksAsync(reportId, request, cancellationToken);

    public Task<Result> DeleteLinksAsync(
        Guid reportId,
        DeleteJobLinksRequest request,
        CancellationToken cancellationToken) =>
        inner.DeleteLinksAsync(reportId, request, cancellationToken);

    public Task<Result<JobDeleteErrorResponse>> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        inner.DeleteAsync(id, cancellationToken);

    public Task<Result<JobReportSummaryResponse>> RestoreDeletionAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        inner.RestoreDeletionAsync(id, cancellationToken);

    public Task<Result> MarkJobAsSeenAsync(
        Guid id,
        string? viewType,
        CancellationToken cancellationToken) =>
        inner.MarkJobAsSeenAsync(id, viewType, cancellationToken);

    public Task InvalidateJobDetailCacheAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken) =>
        inner.InvalidateJobDetailCacheAsync(id, organizationId, cancellationToken);
}
