using Workslip.Api.Endpoints;
using Workslip.Application.Jobs;
using Workslip.Domain;
using Xunit;

namespace Workslip.Tests.Configuration;

public sealed class AuditorCacheIsolationTests
{
    [Fact]
    public void JobReportEtag_changes_when_role_scoped_installations_change()
    {
        var report = CreateReport("Vand", "Varme");
        var auditorReport = AuditorDataScope.Filter(report);

        Assert.NotNull(auditorReport);
        Assert.NotEqual(
            HttpCacheHeaders.JobReportEtag(report),
            HttpCacheHeaders.JobReportEtag(auditorReport!));
    }

    private static JobReportSummaryResponse CreateReport(params string[] installationTypes)
    {
        var now = DateTimeOffset.UnixEpoch;
        var types = installationTypes.Select((name, index) => new InstallationTypeResponse(
            Guid.NewGuid(),
            name,
            index + 1,
            Array.Empty<InstallationTypeCategoryResponse>())).ToArray();

        return new JobReportSummaryResponse(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Test organization",
            "12345678",
            "0001",
            JobStatus.Approved,
            null,
            new CustomerSnapshotResponse(null, null, null, null, null),
            null,
            null,
            null,
            JobType.KLS.ToString(),
            new JobReportSummaryWorkResponse(null, types, [], null),
            new JobReportSummaryObservationResponse(null, null, null),
            [],
            [],
            now,
            now,
            null,
            [],
            [],
            null,
            null,
            false,
            null);
    }
}
