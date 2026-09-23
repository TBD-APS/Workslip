using Workslip.Application.Analytics;
using Workslip.Domain;
using Xunit;

namespace Workslip.Tests.Analytics;

public sealed class ActivationMetricSemanticsTests
{
    [Fact]
    public void ResolveFirstCompletedCompliantAt_UsesFirstApprovalTransition()
    {
        var first = DateTimeOffset.Parse("2026-09-01T08:00:00Z");
        var second = DateTimeOffset.Parse("2026-09-15T09:00:00Z");

        var result = ActivationMetricSemantics.ResolveFirstCompletedCompliantAt(
            JobStatus.Approved.ToString(),
            second.AddDays(1),
            [
                new(JobStatus.InReview.ToString(), JobStatus.Approved.ToString(), first),
                new(JobStatus.Approved.ToString(), JobStatus.Reopened.ToString(), first.AddDays(1)),
                new(JobStatus.InReview.ToString(), JobStatus.Approved.ToString(), second)
            ]);

        Assert.Equal(first, result);
    }

    [Fact]
    public void ResolveFirstCompletedCompliantAt_PreservesHistoricActivationAfterReopen()
    {
        var approval = DateTimeOffset.Parse("2026-09-01T08:00:00Z");

        var result = ActivationMetricSemantics.ResolveFirstCompletedCompliantAt(
            JobStatus.Reopened.ToString(),
            approval.AddDays(2),
            [new(JobStatus.InReview.ToString(), JobStatus.Approved.ToString(), approval)]);

        Assert.Equal(approval, result);
    }

    [Fact]
    public void ResolveFirstCompletedCompliantAt_UsesUpdatedAtOnlyForLegacyCurrentApprovedJob()
    {
        var updatedAt = DateTimeOffset.Parse("2026-08-10T12:00:00Z");

        var result = ActivationMetricSemantics.ResolveFirstCompletedCompliantAt(
            JobStatus.Approved.ToString(),
            updatedAt,
            []);

        Assert.Equal(updatedAt, result);
    }

    [Fact]
    public void ResolveFirstCompletedCompliantAt_DoesNotTreatSubmissionAsActivation()
    {
        var result = ActivationMetricSemantics.ResolveFirstCompletedCompliantAt(
            JobStatus.InReview.ToString(),
            DateTimeOffset.Parse("2026-09-01T08:00:00Z"),
            [new(JobStatus.Draft.ToString(), JobStatus.InReview.ToString(), DateTimeOffset.Parse("2026-09-01T07:00:00Z"))]);

        Assert.Null(result);
    }

    [Fact]
    public void Percentile_UsesNearestRank()
    {
        var values = new[] { 1d, 2d, 3d, 4d };

        Assert.Equal(2d, ActivationMetricSemantics.Percentile(values, 0.50));
        Assert.Equal(3d, ActivationMetricSemantics.Percentile(values, 0.75));
    }
}
