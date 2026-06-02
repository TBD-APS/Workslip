using Workslip.Api.Endpoints;
using Workslip.Application.Jobs;
using Workslip.Domain;
using Xunit;

namespace Workslip.Tests.Configuration;

public sealed class HttpCacheHeadersTests
{
    [Fact]
    public void JobReportEtag_changes_when_links_change()
    {
        var report = CreateReport(links: []);
        var changedReport = report with
        {
            Links =
            [
                new JobLinkInfoResponse(Guid.NewGuid(), Guid.NewGuid(), "2026-001", "ACME", JobStatus.Draft.ToString())
            ]
        };

        Assert.NotEqual(HttpCacheHeaders.JobReportEtag(report), HttpCacheHeaders.JobReportEtag(changedReport));
    }

    [Fact]
    public void JobReportEtag_changes_when_assigned_users_change()
    {
        var report = CreateReport(assignedUsers: []);
        var changedReport = report with
        {
            AssignedUsers = [new AssignedUserResponse(Guid.NewGuid(), "Ada")]
        };

        Assert.NotEqual(HttpCacheHeaders.JobReportEtag(report), HttpCacheHeaders.JobReportEtag(changedReport));
    }

    private static JobReportSummaryResponse CreateReport(
        IReadOnlyList<JobLinkInfoResponse>? links = null,
        IReadOnlyList<AssignedUserResponse>? assignedUsers = null)
    {
        return new JobReportSummaryResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "2026-000",
            JobStatus.Draft,
            new CustomerInfo(null, null, null, null, null, null),
            new JobReportSummaryWorkResponse(null, [], [], null),
            new JobReportSummaryObservationResponse(null, null, null, null),
            [],
            links ?? [],
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            assignedUsers ?? [],
            false,
            null);
    }
}
