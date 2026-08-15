using Ardalis.Result;
using Workslip.Domain;

namespace Workslip.Application.Jobs;

public sealed record JobOverviewResponse(
    int ActiveCount,
    int InReviewCount,
    int ApprovedCount,
    int RejectedCount,
    IReadOnlyList<JobListItemResponse> RecentJobs);

public interface IJobOverviewService
{
    Task<Result<JobOverviewResponse>> GetAsync(CancellationToken cancellationToken);
}

public sealed class JobOverviewService(IJobService jobService) : IJobOverviewService
{
    private const int RecentJobLimit = 6;
    private static readonly List<JobStatus> AllStatuses =
    [
        JobStatus.Draft,
        JobStatus.InReview,
        JobStatus.Approved,
        JobStatus.Rejected
    ];

    public async Task<Result<JobOverviewResponse>> GetAsync(CancellationToken cancellationToken)
    {
        var active = await CountAsync(JobStatus.Draft, cancellationToken);
        if (!active.IsSuccess) return Result<JobOverviewResponse>.Unauthorized();

        var inReview = await CountAsync(JobStatus.InReview, cancellationToken);
        if (!inReview.IsSuccess) return Result<JobOverviewResponse>.Unauthorized();

        var approved = await CountAsync(JobStatus.Approved, cancellationToken);
        if (!approved.IsSuccess) return Result<JobOverviewResponse>.Unauthorized();

        var rejected = await CountAsync(JobStatus.Rejected, cancellationToken);
        if (!rejected.IsSuccess) return Result<JobOverviewResponse>.Unauthorized();

        var recent = await jobService.ListAsync(
            AllStatuses,
            reportNumber: null,
            customerName: null,
            customerEmail: null,
            customerAddress: null,
            search: null,
            sortBy: "updatedAt",
            sortDirection: "desc",
            limit: RecentJobLimit,
            offset: 0,
            cancellationToken: cancellationToken);
        if (!recent.IsSuccess) return Result<JobOverviewResponse>.Unauthorized();

        return Result<JobOverviewResponse>.Success(new JobOverviewResponse(
            active.Value,
            inReview.Value,
            approved.Value,
            rejected.Value,
            recent.Value.Items));
    }

    private async Task<Result<int>> CountAsync(JobStatus status, CancellationToken cancellationToken)
    {
        var result = await jobService.ListAsync(
            [status],
            reportNumber: null,
            customerName: null,
            customerEmail: null,
            customerAddress: null,
            search: null,
            sortBy: null,
            sortDirection: null,
            limit: 1,
            offset: 0,
            cancellationToken: cancellationToken);

        return result.IsSuccess
            ? Result<int>.Success(result.Value.TotalCount)
            : Result<int>.Unauthorized();
    }
}
