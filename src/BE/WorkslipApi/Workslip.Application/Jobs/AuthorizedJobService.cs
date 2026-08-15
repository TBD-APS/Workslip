using Ardalis.Result;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;
using Workslip.Domain;

namespace Workslip.Application.Jobs;

public sealed class AuthorizedJobService(
    JobService inner,
    IJobRepository jobRepository,
    IJobAuditorScopeRepository auditorScopeRepository,
    ICurrentUserContext currentUser,
    ILogger<AuthorizedJobService> logger) : IJobService
{
    private const int AuditorScanPageSize = 200;

    public async Task<Result<JobReportSummaryResponse>> CreateAsync(
        CreateJobRequest request,
        CancellationToken cancellationToken)
    {
        if (JobAssignmentPolicy.RequiresAssignedJobScope(currentUser.Role))
        {
            foreach (var linkedJobId in request.LinkedJobIds ?? [])
            {
                if (await GetAccessibleJobAsync(linkedJobId, cancellationToken) is null)
                {
                    return Result<JobReportSummaryResponse>.NotFound();
                }
            }
        }

        return await inner.CreateAsync(request, cancellationToken);
    }

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

        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
            return Result<JobListResponse>.Unauthorized();

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
                return page;

            rawTotalCount = page.Value.TotalCount;
            var inScopeIds = await auditorScopeRepository.GetVisibleJobIdsAsync(
                organizationId.Value,
                page.Value.Items.Select(item => item.Id).ToArray(),
                cancellationToken);

            foreach (var item in page.Value.Items)
            {
                if (!inScopeIds.Contains(item.Id))
                    continue;

                var filtered = AuditorDataScope.Filter(item);
                if (filtered is not null)
                    visibleItems.Add(filtered);
            }

            if (page.Value.Items.Count == 0)
                break;

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
            return result;

        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
            return Result<IReadOnlyList<JobListItemResponse>>.Unauthorized();

        var inScopeIds = await auditorScopeRepository.GetVisibleJobIdsAsync(
            organizationId.Value,
            result.Value.Select(item => item.Id).ToArray(),
            cancellationToken);

        var visibleItems = result.Value
            .Where(item => inScopeIds.Contains(item.Id))
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
        if (RequiresScopedJobAccess()
            && await GetAccessibleJobAsync(id, cancellationToken) is null)
        {
            return Result<JobReportSummaryResponse>.NotFound();
        }

        var result = await inner.GetSingleJobAsync(id, cancellationToken);
        if (!result.IsSuccess || !RequiresScopedJobAccess())
            return result;

        var filtered = AuditorDataScope.AppliesTo(currentUser.Role)
            ? AuditorDataScope.Filter(result.Value)
            : result.Value;
        if (filtered is null)
            return Result<JobReportSummaryResponse>.NotFound();

        var visibleLinks = await FilterVisibleLinksAsync(filtered.Links, cancellationToken);
        return Result<JobReportSummaryResponse>.Success(filtered with { Links = visibleLinks });
    }

    public async Task<Result<IReadOnlyList<JobHistoryResponse>>> GetHistoryAsync(
        Guid id,
        int? limit,
        int? offset,
        CancellationToken cancellationToken)
    {
        if (!RequiresScopedJobAccess())
            return await inner.GetHistoryAsync(id, limit, offset, cancellationToken);

        if (await GetAccessibleJobAsync(id, cancellationToken) is null)
            return Result<IReadOnlyList<JobHistoryResponse>>.NotFound();

        if (!AuditorDataScope.AppliesTo(currentUser.Role))
            return await inner.GetHistoryAsync(id, limit, offset, cancellationToken);

        var requestedLimit = Math.Clamp(limit ?? 50, 1, AuditorScanPageSize);
        var requestedOffset = Math.Max(offset ?? 0, 0);
        var visibleEvents = new List<JobHistoryResponse>();
        var scanOffset = 0;

        while (true)
        {
            var page = await inner.GetHistoryAsync(id, AuditorScanPageSize, scanOffset, cancellationToken);
            if (!page.IsSuccess)
                return page;

            visibleEvents.AddRange(AuditorDataScope.Filter(page.Value));

            if (page.Value.Count < AuditorScanPageSize)
                break;

            scanOffset += page.Value.Count;
        }

        var requestedEvents = visibleEvents
            .Skip(requestedOffset)
            .Take(requestedLimit)
            .ToArray();

        return Result<IReadOnlyList<JobHistoryResponse>>.Success(requestedEvents);
    }

    public async Task<Result<JobReportSummaryResponse>> UpdateAsync(
        Guid id,
        UpdateJobRequest request,
        CancellationToken cancellationToken)
    {
        if (JobAssignmentPolicy.RequiresAssignedJobScope(currentUser.Role)
            && await GetAccessibleJobAsync(id, cancellationToken) is null)
        {
            return Result<JobReportSummaryResponse>.NotFound();
        }

        return await inner.UpdateAsync(id, request, cancellationToken);
    }

    public async Task<Result<JobReportSummaryResponse>> ChangeStatusAsync(
        Guid id,
        ChangeJobStatusRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
            return Result<JobReportSummaryResponse>.Unauthorized();

        var job = RequiresScopedJobAccess()
            ? await GetAccessibleJobAsync(id, cancellationToken)
            : await jobRepository.GetSingleJobAsync(id, organizationId.Value, cancellationToken);
        if (job is null)
            return Result<JobReportSummaryResponse>.NotFound();

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

    public async Task<Result<JobReportSummaryResponse>> CreateLinksAsync(
        Guid reportId,
        CreateJobLinkRequest request,
        CancellationToken cancellationToken)
    {
        if (JobAssignmentPolicy.RequiresAssignedJobScope(currentUser.Role))
        {
            var source = await GetAccessibleJobAsync(reportId, cancellationToken);
            if (source is null)
                return Result<JobReportSummaryResponse>.NotFound();

            foreach (var targetId in request.TargetReportIds)
            {
                if (await GetAccessibleJobAsync(targetId, cancellationToken) is null)
                    return Result<JobReportSummaryResponse>.NotFound();
            }
        }

        return await inner.CreateLinksAsync(reportId, request, cancellationToken);
    }

    public async Task<Result> DeleteLinksAsync(
        Guid reportId,
        DeleteJobLinksRequest request,
        CancellationToken cancellationToken)
    {
        if (JobAssignmentPolicy.RequiresAssignedJobScope(currentUser.Role))
        {
            var source = await GetAccessibleJobAsync(reportId, cancellationToken);
            if (source is null)
                return Result.NotFound();

            var linkedJobIds = source.Links
                .Where(link => request.LinkIds.Contains(link.Id))
                .Select(link => link.LinkedReportId);
            foreach (var linkedJobId in linkedJobIds)
            {
                if (await GetAccessibleJobAsync(linkedJobId, cancellationToken) is null)
                    return Result.NotFound();
            }
        }

        return await inner.DeleteLinksAsync(reportId, request, cancellationToken);
    }

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
        if (!RequiresScopedJobAccess())
            return await inner.MarkJobAsSeenAsync(id, viewType, cancellationToken);

        if (await GetAccessibleJobAsync(id, cancellationToken) is null)
            return Result.NotFound();

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
        if (currentUser.OrganizationId is null || links.Count == 0)
            return Array.Empty<JobLinkInfoResponse>();

        var visibleLinks = new List<JobLinkInfoResponse>(links.Count);
        foreach (var link in links)
        {
            if (await GetAccessibleJobAsync(link.LinkedReportId, cancellationToken) is not null)
                visibleLinks.Add(link);
        }

        return visibleLinks;
    }

    private bool RequiresScopedJobAccess() =>
        JobAssignmentPolicy.RequiresAssignedJobScope(currentUser.Role)
        || AuditorDataScope.AppliesTo(currentUser.Role);

    private bool CanAccessJob(JobReportResponse job)
    {
        if (AuditorDataScope.AppliesTo(currentUser.Role)
            && !AuditorDataScope.CanAccess(job))
        {
            return false;
        }

        return !JobAssignmentPolicy.RequiresAssignedJobScope(currentUser.Role)
            || currentUser.UserId is Guid userId
            && job.AssignedUsers.Any(assignee => assignee.Id == userId);
    }

    private async Task<JobReportResponse?> GetAccessibleJobAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
            return null;

        if (AuditorDataScope.AppliesTo(currentUser.Role))
        {
            var scope = await auditorScopeRepository.GetAsync(id, organizationId.Value, cancellationToken);
            if (scope is null || !scope.IsInAuditorScope)
                return null;
        }

        var job = await jobRepository.GetSingleJobAsync(id, organizationId.Value, cancellationToken);
        return job is not null && CanAccessJob(job)
            ? job
            : null;
    }
}
