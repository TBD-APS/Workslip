namespace Workslip.Domain;

public enum JobStatus
{
    Draft,
    Submitted,
    InReview,
    Approved,
    Rejected,
    Archived
}

public static class JobStatusPolicy
{
    public static bool CanEdit(JobStatus status) =>
        status is JobStatus.Draft or JobStatus.Submitted or JobStatus.InReview or JobStatus.Rejected;

    public static bool CanTransition(JobStatus current, JobStatus next) =>
        (current, next) switch
        {
            (JobStatus.Draft, JobStatus.Submitted) => true,
            (JobStatus.Approved, JobStatus.Archived) => true,
            (JobStatus.Rejected, JobStatus.Submitted) => true,
            (JobStatus.Submitted, JobStatus.InReview) => true,
            (JobStatus.Submitted, JobStatus.Approved) => true,
            (JobStatus.Submitted, JobStatus.Rejected) => true,
            (JobStatus.InReview, JobStatus.Approved) => true,
            (JobStatus.InReview, JobStatus.Rejected) => true,
            (JobStatus.InReview, JobStatus.Submitted) => true,
            _ => false
        };
}
