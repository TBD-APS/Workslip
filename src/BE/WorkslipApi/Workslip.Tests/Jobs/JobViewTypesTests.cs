using Workslip.Application.Jobs;
using Workslip.Domain;
using Xunit;

namespace Workslip.Tests.Jobs;

public sealed class JobViewTypesTests
{
    [Fact]
    public void Approved_job_requires_completed_view_even_when_job_was_seen_before_approval()
    {
        var isSeen = JobViewTypes.IsSeen(
            JobStatus.Approved,
            hasNewView: true,
            hasCompletedView: false);

        Assert.False(isSeen);
    }

    [Fact]
    public void Approved_job_is_seen_after_completed_view()
    {
        var isSeen = JobViewTypes.IsSeen(
            JobStatus.Approved,
            hasNewView: true,
            hasCompletedView: true);

        Assert.True(isSeen);
    }

    [Theory]
    [InlineData(JobStatus.Draft)]
    [InlineData(JobStatus.InReview)]
    [InlineData(JobStatus.Rejected)]
    public void Non_approved_job_uses_normal_view(JobStatus status)
    {
        var isSeen = JobViewTypes.IsSeen(
            status,
            hasNewView: true,
            hasCompletedView: false);

        Assert.True(isSeen);
    }
}
