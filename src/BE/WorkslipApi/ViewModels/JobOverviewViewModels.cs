namespace Workslip.Api.ViewModels;

public sealed record JobOverviewRecentJobViewModel(
    Guid Id,
    string? ReportNumber,
    string Status,
    string? CustomerName,
    string? CustomerNumber,
    string? Address,
    DateTimeOffset UpdatedAt);

public sealed record JobOverviewViewModel(
    int ActiveCount,
    int InReviewCount,
    int ApprovedCount,
    int RejectedCount,
    IReadOnlyList<JobOverviewRecentJobViewModel> RecentJobs);
