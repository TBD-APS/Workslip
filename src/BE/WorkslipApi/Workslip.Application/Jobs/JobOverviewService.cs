using Ardalis.Result;
using Workslip.Application.Customers;
using Workslip.Domain;

namespace Workslip.Application.Jobs;

public sealed record JobOverviewRecentJobResponse(
    Guid Id,
    string? ReportNumber,
    JobStatus Status,
    string? CustomerName,
    string? CustomerNumber,
    string? Address,
    DateTimeOffset UpdatedAt);

public sealed record JobOverviewResponse(
    int ActiveCount,
    int InReviewCount,
    int ApprovedCount,
    int RejectedCount,
    IReadOnlyList<JobOverviewRecentJobResponse> RecentJobs);

public interface IJobOverviewService
{
    Task<Result<JobOverviewResponse>> GetAsync(CancellationToken cancellationToken);
}

public sealed class JobOverviewService(IJobService jobService, ICustomerService customerService) : IJobOverviewService
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

        var recentJobs = new List<JobOverviewRecentJobResponse>(recent.Value.Items.Count);
        foreach (var job in recent.Value.Items)
        {
            string? customerNumber = null;
            if (job.Customer?.CustomerId is Guid customerId && customerId != Guid.Empty)
            {
                var customer = await customerService.GetByIdAsync(customerId, cancellationToken);
                if (customer.IsSuccess)
                {
                    customerNumber = customer.Value.CustomerNumber;
                }
            }

            recentJobs.Add(new JobOverviewRecentJobResponse(
                job.Id,
                job.ReportNumber,
                job.Status,
                job.Customer?.Name,
                customerNumber,
                job.DestinationAddress ?? job.Customer?.Address,
                job.UpdatedAt));
        }

        return Result<JobOverviewResponse>.Success(new JobOverviewResponse(
            active.Value,
            inReview.Value,
            approved.Value,
            rejected.Value,
            recentJobs));
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
