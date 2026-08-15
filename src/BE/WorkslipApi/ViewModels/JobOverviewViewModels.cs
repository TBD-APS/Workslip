namespace Workslip.Api.ViewModels;

public sealed record JobOverviewViewModel(
    int ActiveCount,
    int InReviewCount,
    int ApprovedCount,
    int RejectedCount,
    IReadOnlyList<JobListItemViewModel> RecentJobs);
