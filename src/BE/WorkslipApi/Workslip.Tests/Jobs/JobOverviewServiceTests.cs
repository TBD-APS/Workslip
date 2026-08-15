using Ardalis.Result;
using Workslip.Application.Customers;
using Workslip.Application.Jobs;
using Workslip.Domain;
using Xunit;

namespace Workslip.Tests.Jobs;

public sealed class JobOverviewServiceTests
{
    [Fact]
    public async Task GetAsync_returns_status_counts_and_requests_six_recent_jobs()
    {
        var jobService = new StubJobService(new Dictionary<JobStatus, int>
        {
            [JobStatus.Draft] = 7,
            [JobStatus.InReview] = 3,
            [JobStatus.Approved] = 11,
            [JobStatus.Rejected] = 2,
        });
        var service = new JobOverviewService(jobService, new StubCustomerRepository());

        var result = await service.GetAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value.ActiveCount);
        Assert.Equal(3, result.Value.InReviewCount);
        Assert.Equal(11, result.Value.ApprovedCount);
        Assert.Equal(2, result.Value.RejectedCount);
        Assert.Empty(result.Value.RecentJobs);
        Assert.Equal(5, jobService.ListCalls.Count);

        var recentCall = jobService.ListCalls[^1];
        Assert.Equal(6, recentCall.Limit);
        Assert.Equal(0, recentCall.Offset);
        Assert.Equal("updatedAt", recentCall.SortBy);
        Assert.Equal("desc", recentCall.SortDirection);
        Assert.Equal(
            new[] { JobStatus.Draft, JobStatus.InReview, JobStatus.Approved, JobStatus.Rejected },
            recentCall.Statuses);
    }

    private sealed record ListCall(
        List<JobStatus>? Statuses,
        string? SortBy,
        string? SortDirection,
        int? Limit,
        int? Offset);

    private sealed class StubJobService(IReadOnlyDictionary<JobStatus, int> counts) : IJobService
    {
        public List<ListCall> ListCalls { get; } = [];

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
            ListCalls.Add(new ListCall(statuses, sortBy, sortDirection, limit, offset));
            var totalCount = statuses is { Count: 1 } && counts.TryGetValue(statuses[0], out var count)
                ? count
                : 0;
            return Task.FromResult(Result<JobListResponse>.Success(new JobListResponse([], totalCount)));
        }

        public Task<Result<JobReportSummaryResponse>> CreateAsync(CreateJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<JobListItemResponse>>> GetMyAssignedJobsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> GetSingleJobAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<JobHistoryResponse>>> GetHistoryAsync(Guid id, int? limit, int? offset, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> UpdateAsync(Guid id, UpdateJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> ChangeStatusAsync(Guid id, ChangeJobStatusRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> AssignAsync(Guid jobId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> CreateLinksAsync(Guid reportId, CreateJobLinkRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> DeleteLinksAsync(Guid reportId, DeleteJobLinksRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobDeleteErrorResponse>> DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<JobReportSummaryResponse>> RestoreDeletionAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> MarkJobAsSeenAsync(Guid id, string? viewType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task InvalidateJobDetailCacheAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubCustomerRepository : ICustomerRepository
    {
        public Task<Guid> CreateCustomerAsync(Guid organizationId, CustomerData customer, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CustomerListItemResponse>> ListAsync(Guid organizationId, int limit, int offset, string? search, string? sortBy, string? sortDirection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> GetCustomerCountAsync(Guid organizationId, string? search, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerDetailResponse?> GetByIdAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) => Task.FromResult<CustomerDetailResponse?>(null);
        public Task UpdateAsync(Guid organizationId, Guid id, CustomerData customer, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetFavoriteAsync(Guid organizationId, Guid id, bool isFavorite, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(Guid organizationId, Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CustomerSearchResponse>> SearchAsync(Guid organizationId, string query, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CustomerSearchResponse>> GetFavoriteCustomersAsync(Guid organizationId, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetExistingCustomerNumbersAsync(Guid organizationId, IReadOnlyCollection<string> customerNumbers, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerBulkCreateResult> BulkCreateAsync(Guid organizationId, IReadOnlyList<CustomerData> customers, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
