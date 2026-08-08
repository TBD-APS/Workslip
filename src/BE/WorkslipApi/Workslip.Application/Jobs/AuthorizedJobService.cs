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
    private const int AuditorScanPageSize = 200;

    public Task<Result<JobReportSummaryResponse>> CreateAsync(
        CreateJobRequest request,
        CancellationToken cancellationToken) =>
        inner.CreateAsync(request, cancellationToken);

    public async Task<Result<JobListResponse>> ListAsync(
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
        CancellationToken cancellationToken)
    {
        if (!AuditorDataScope.AppliesTo(currentUser.Role))
        {
            return await inner.ListAsync(
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
        }

        var requestedLimit = Math.Clamp(limit ?? 50, 1, AuditorScanPageSize);
        var requestedOffset = Math.Max(offset ?? 0, 0);
        var visibleItems = new List<JobListItemResponse>();
        var scanOffset = 0;
        var rawTotalCount = int.MaxValue;

        do
        {
            var page = await inner.ListAsync(
                statuses,
                reportNumber,
                customerName,
                customerEmail,
                customerAddress,
                search,
                sortBy,
                sortDirection,
                AuditorScanPageSize,
                scanOffset,
                cancellationToken);

            if (!page.IsSuccess)
            {
                return page;
            }

            rawTotalCount = page.Value.TotalCount;
            foreach (var item in page.Value.Items)
            {
                var filtered = AuditorDataScope.Filter(item);
                if (filtered is not null)
                {
                    visibleItems.Add(filtered);
                }
            }

            if (page.Value.Items.Count == 0)
            {
                break;
            }

            scanOffset += page.Value.Items.Count;
        }
        while (scanOffset < rawTotalCount);

        var requestedItems = visibleItems
            .Skip(requestedOffset)
            .Take(requestedLimit)
            .ToArray();

        return Result<JobListResponse>.Success(new JobListResponse(requestedItems, visibleItems.Count));
    }

    public async Task<Result<IReadOnlyList<JobListItemResponse>>> GetMyAssignedJobsAsync(
        CancellationToken cancellationToken)
    {
        var result = await inner.GetMyAssignedJobsAsync(cancellationToken);
        if (!result.IsSuccess || !AuditorDataScope.AppliesTo(currentUser.Role))
        {
            return result;
        }

        var visibleItems = result.Value
            .Select(AuditorDataScope.Filter)
            .Where(item => item is not null)
            .Cast<JobListItemResponse>()
            .ToArray();

        return Result<IReadOnlyList<JobListItemResponse>>.Success(visibleItems);
    }

    public async Task<Result<JobReportSummaryResponse>> GetSingleJobAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await inner.GetSingleJobAsync(id, cancellationToken);
        if (!result.IsSuccess || !AuditorDataScope.AppliesTo(currentUser.Role))
        {
            return result;
        }

        var filtered = AuditorDataScope.Filter(result.Value);
        if (filtered is null)
        {
            return Result<JobReportSummaryResponse>.NotFound();
        }

        var visibleLinks = await FilterVisibleLinksAsync(filtered.Links, cancellationToken);
        return Result<JobReportSummaryResponse>.Success(filtered with { Links = visibleLinks });
    }

    public async Task<Result<IReadOnlyList<JobHistoryResponse>>> GetHistoryAsync(
        Guid id,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        if (!AuditorDataScope.AppliesTo(currentUser.Role))
        {
            return await inner.GetHistoryAsync(id, limit, offset, cancellationToken);
        }

        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<IReadOnlyList<JobHistoryResponse>>.Unauthorized();
        }

        var job = await jobRepository.GetSingleJobAsync(id, organizationId.Value, cancellationToken);
        if (job is null || !AuditorDataScope.CanAccess(job))
        {
            return Result<IReadOnlyList<JobHistoryResponse>>.NotFound();
        }

        var result = await inner.GetHistoryAsync(id, limit, offset, cancellationToken);
        if (!result.IsSuccess)
        {
            return result;
        }

        return Result<IReadOnlyList<JobHistoryResponse>>.Success(AuditorDataScope.Filter(result.Value));
    }

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

        try
        {
            return await inner.ChangeStatusAsync(id, request, cancellationToken);
        }
        catch (InvalidJobStatusTransitionException exception)
        {
            logger.LogWarning(
                exception,
                "Concurrent job transition rejected. JobId: {JobId}. OrganizationId: {OrganizationId}. CurrentStatus: {CurrentStatus}. TargetStatus: {TargetStatus}.",
                id,
                organizationId.Value,
                exception.CurrentStatus,
                exception.TargetStatus);

            return Result<JobReportSummaryResponse>.Conflict("invalid_job_status_transition");
        }
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

    public async Task<Result> MarkJobAsSeenAsync(
        Guid id,
        string? viewType,
        CancellationToken cancellationToken)
    {
        if (!AuditorDataScope.AppliesTo(currentUser.Role))
        {
            return await inner.MarkJobAsSeenAsync(id, viewType, cancellationToken);
        }

        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result.Unauthorized();
        }

        var job = await jobRepository.GetSingleJobAsync(id, organizationId.Value, cancellationToken);
        if (job is null || !AuditorDataScope.CanAccess(job))
        {
            return Result.NotFound();
        }

        return await inner.MarkJobAsSeenAsync(id, viewType, cancellationToken);
    }

    public Task InvalidateJobDetailCacheAsync(
        Guid id,
        Guid organizationId,
        CancellationToken cancellationToken) =>
        inner.InvalidateJobDetailCacheAsync(id, organizationId, cancellationToken);

    private async Task<IReadOnlyList<JobLinkInfoResponse>> FilterVisibleLinksAsync(
        IReadOnlyList<JobLinkInfoResponse> links,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null || links.Count == 0)
        {
            return Array.Empty<JobLinkInfoResponse>();
        }

        var visibleLinks = new List<JobLinkInfoResponse>(links.Count);
        foreach (var link in links)
        {
            var linkedJob = await jobRepository.GetSingleJobAsync(
                link.LinkedReportId,
                organizationId.Value,
                cancellationToken);

            if (linkedJob is not null && AuditorDataScope.CanAccess(linkedJob))
            {
                visibleLinks.Add(link);
            }
        }

        return visibleLinks;
    }
}
