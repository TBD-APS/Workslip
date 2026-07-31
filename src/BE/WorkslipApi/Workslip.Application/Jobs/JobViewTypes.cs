using Workslip.Domain;

namespace Workslip.Application.Jobs;

public static class JobViewTypes
{
    public const string New = "New";
    public const string Completed = "Completed";

    public static bool IsSeen(JobStatus status, bool hasNewView, bool hasCompletedView) =>
        status == JobStatus.Approved ? hasCompletedView : hasNewView;
}
