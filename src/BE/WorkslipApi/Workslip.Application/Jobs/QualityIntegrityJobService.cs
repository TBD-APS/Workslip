using Ardalis.Result;
using Workslip.Domain;

namespace Workslip.Application.Jobs;

/// <summary>
/// Product-facing quality integrity boundary. It turns attempts to mutate an
/// approved job into a predictable conflict before persistence is touched.
/// The persistence interceptor remains the final fail-closed safety net.
/// </summary>
public sealed class QualityIntegrityJobService(AuthorizedJobService inner) : IJobService
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
        CancellationToken cancellationToken)
    {
        var effectiveStatuses = statuses is not null
            && statuses.Contains(JobStatus.Draft)
            && !statuses.Contains(JobStatus.Reopened)
                ? [.. statuses, JobStatus.Reopened]
                : statuses;

        return inner.ListAsync(
            effectiveStatuses,
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

    public async Task<Result<JobReportSummaryResponse>> UpdateAsync(
        Guid id,
        UpdateJobRequest request,
        CancellationToken cancellationToken)
    {
        var locked = await IsApprovedAsync(id, cancellationToken);
        if (!locked.IsSuccess)
            return MapLookupFailure<JobReportSummaryResponse>(locked);
        if (locked.Value)
            return Result<JobReportSummaryResponse>.Conflict("approved_job_locked");

        return await inner.UpdateAsync(id, request, cancellationToken);
    }

    public Task<Result<JobReportSummaryResponse>> ChangeStatusAsync(
        Guid id,
        ChangeJobStatusRequest request,
        CancellationToken cancellationToken) =>
        inner.ChangeStatusAsync(id, request, cancellationToken);

    public async Task<Result<JobReportSummaryResponse>> CreateLinksAsync(
        Guid reportId,
        CreateJobLinkRequest request,
        CancellationToken cancellationToken)
    {
        var locked = await IsApprovedAsync(reportId, cancellationToken);
        if (!locked.IsSuccess)
            return MapLookupFailure<JobReportSummaryResponse>(locked);
        if (locked.Value)
            return Result<JobReportSummaryResponse>.Conflict("approved_job_locked");

        return await inner.CreateLinksAsync(reportId, request, cancellationToken);
    }

    public async Task<Result> DeleteLinksAsync(
        Guid reportId,
        DeleteJobLinksRequest request,
        CancellationToken cancellationToken)
    {
        var locked = await IsApprovedAsync(reportId, cancellationToken);
        if (!locked.IsSuccess)
            return MapLookupFailure(locked);
        if (locked.Value)
            return Result.Conflict("approved_job_locked");

        return await inner.DeleteLinksAsync(reportId, request, cancellationToken);
    }

    public async Task<Result<JobDeleteErrorResponse>> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var locked = await IsApprovedAsync(id, cancellationToken);
        if (!locked.IsSuccess)
            return MapLookupFailure<JobDeleteErrorResponse>(locked);
        if (locked.Value)
            return Result<JobDeleteErrorResponse>.Conflict("approved_job_locked");

        return await inner.DeleteAsync(id, cancellationToken);
    }

    public async Task<Result<JobReportSummaryResponse>> RestoreDeletionAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var locked = await IsApprovedAsync(id, cancellationToken);
        if (!locked.IsSuccess)
            return MapLookupFailure<JobReportSummaryResponse>(locked);
        if (locked.Value)
            return Result<JobReportSummaryResponse>.Conflict("approved_job_locked");

        return await inner.RestoreDeletionAsync(id, cancellationToken);
    }

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

    private async Task<Result<bool>> IsApprovedAsync(Guid id, CancellationToken cancellationToken)
    {
        var job = await inner.GetSingleJobAsync(id, cancellationToken);
        return job.Status switch
        {
            ResultStatus.Ok => Result<bool>.Success(job.Value.Status == JobStatus.Approved),
            ResultStatus.Unauthorized => Result<bool>.Unauthorized(),
            ResultStatus.Forbidden => Result<bool>.Forbidden(),
            _ => Result<bool>.NotFound()
        };
    }

    private static Result<T> MapLookupFailure<T>(Result<bool> result) => result.Status switch
    {
        ResultStatus.Unauthorized => Result<T>.Unauthorized(),
        ResultStatus.Forbidden => Result<T>.Forbidden(),
        _ => Result<T>.NotFound()
    };

    private static Result MapLookupFailure(Result<bool> result) => result.Status switch
    {
        ResultStatus.Unauthorized => Result.Unauthorized(),
        ResultStatus.Forbidden => Result.Forbidden(),
        _ => Result.NotFound()
    };
}
