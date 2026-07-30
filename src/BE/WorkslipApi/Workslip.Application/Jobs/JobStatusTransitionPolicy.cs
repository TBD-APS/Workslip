using Workslip.Domain;

namespace Workslip.Application.Jobs;

public enum JobStatusTransitionDecision
{
    Allowed,
    Forbidden,
    Conflict
}

public static class JobStatusTransitionPolicy
{
    public static JobStatusTransitionDecision Evaluate(
        string? role,
        JobStatus currentStatus,
        JobStatus targetStatus)
    {
        var isUser = string.Equals(role, Roles.User, StringComparison.OrdinalIgnoreCase);
        var isReviewer = string.Equals(role, Roles.Admin, StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase);

        if (!isUser && !isReviewer)
        {
            return JobStatusTransitionDecision.Forbidden;
        }

        if (targetStatus is JobStatus.Approved or JobStatus.Rejected && !isReviewer)
        {
            return JobStatusTransitionDecision.Forbidden;
        }

        if (targetStatus == JobStatus.Draft)
        {
            return JobStatusTransitionDecision.Conflict;
        }

        if (currentStatus == targetStatus)
        {
            return JobStatusTransitionDecision.Allowed;
        }

        return (currentStatus, targetStatus) switch
        {
            (JobStatus.Draft, JobStatus.InReview) => JobStatusTransitionDecision.Allowed,
            (JobStatus.Rejected, JobStatus.InReview) => JobStatusTransitionDecision.Allowed,
            (JobStatus.InReview, JobStatus.Approved) when isReviewer => JobStatusTransitionDecision.Allowed,
            (JobStatus.InReview, JobStatus.Rejected) when isReviewer => JobStatusTransitionDecision.Allowed,
            _ => JobStatusTransitionDecision.Conflict
        };
    }
}
