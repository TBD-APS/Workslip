namespace Workslip.Domain;

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

        return IsSourceTransitionAllowed(currentStatus, targetStatus)
            ? JobStatusTransitionDecision.Allowed
            : JobStatusTransitionDecision.Conflict;
    }

    public static bool IsSourceTransitionAllowed(
        JobStatus currentStatus,
        JobStatus targetStatus)
    {
        if (targetStatus == JobStatus.Draft)
        {
            return false;
        }

        if (currentStatus == targetStatus)
        {
            return true;
        }

        return (currentStatus, targetStatus) switch
        {
            (JobStatus.Draft, JobStatus.InReview) => true,
            (JobStatus.Rejected, JobStatus.InReview) => true,
            (JobStatus.InReview, JobStatus.Approved) => true,
            (JobStatus.InReview, JobStatus.Rejected) => true,
            _ => false
        };
    }
}

public sealed class InvalidJobStatusTransitionException(
    JobStatus currentStatus,
    JobStatus targetStatus)
    : InvalidOperationException($"Invalid job status transition from {currentStatus} to {targetStatus}.")
{
    public JobStatus CurrentStatus { get; } = currentStatus;
    public JobStatus TargetStatus { get; } = targetStatus;
}
