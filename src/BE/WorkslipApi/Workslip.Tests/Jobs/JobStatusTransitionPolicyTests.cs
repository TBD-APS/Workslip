using Workslip.Application.Jobs;
using Workslip.Domain;
using Xunit;

namespace Workslip.Tests.Jobs;

public sealed class JobStatusTransitionPolicyTests
{
    public static IEnumerable<object?[]> AllRoleAndStatusCombinations()
    {
        var roles = new string?[]
        {
            Roles.User,
            Roles.Admin,
            Roles.Superadmin,
            Roles.Auditor,
            null
        };

        foreach (var role in roles)
        {
            foreach (var currentStatus in Enum.GetValues<JobStatus>())
            {
                foreach (var targetStatus in Enum.GetValues<JobStatus>())
                {
                    yield return
                    [
                        role,
                        currentStatus,
                        targetStatus,
                        ExpectedDecision(role, currentStatus, targetStatus)
                    ];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllRoleAndStatusCombinations))]
    public void Evaluate_enforces_the_complete_role_and_transition_matrix(
        string? role,
        JobStatus currentStatus,
        JobStatus targetStatus,
        JobStatusTransitionDecision expected)
    {
        var actual = JobStatusTransitionPolicy.Evaluate(role, currentStatus, targetStatus);

        Assert.Equal(expected, actual);
    }

    private static JobStatusTransitionDecision ExpectedDecision(
        string? role,
        JobStatus currentStatus,
        JobStatus targetStatus)
    {
        var isUser = role == Roles.User;
        var isReviewer = role is Roles.Admin or Roles.Superadmin;

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

        var allowedTransitions = new HashSet<(JobStatus Current, JobStatus Target)>
        {
            (JobStatus.Draft, JobStatus.InReview),
            (JobStatus.Rejected, JobStatus.InReview),
            (JobStatus.InReview, JobStatus.InReview)
        };

        if (isReviewer)
        {
            allowedTransitions.Add((JobStatus.InReview, JobStatus.Approved));
            allowedTransitions.Add((JobStatus.InReview, JobStatus.Rejected));
            allowedTransitions.Add((JobStatus.Approved, JobStatus.Approved));
            allowedTransitions.Add((JobStatus.Rejected, JobStatus.Rejected));
        }

        return allowedTransitions.Contains((currentStatus, targetStatus))
            ? JobStatusTransitionDecision.Allowed
            : JobStatusTransitionDecision.Conflict;
    }
}
