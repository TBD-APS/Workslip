using System.Text;
using QuestPDF.Infrastructure;
using Workslip.Api.Services;
using Workslip.Application.Jobs;
using Workslip.Application.Worksheets;
using Workslip.Domain;

namespace Workslip.Tests.Infrastructure;

public sealed class JobReportPdfServiceTests
{
    [Fact]
    public void Generate_renders_a_complete_service_report_without_layout_errors()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var jobId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 7, 8, 18, 6, 0, TimeSpan.FromHours(2));
        var category = new InstallationTypeCategoryResponse(Guid.NewGuid(), "Modtagekontrol", 1,
        [
            new InstallationTypeControlPointResponse(Guid.NewGuid(), "Rør og fittings", 1, true, true),
            new InstallationTypeControlPointResponse(Guid.NewGuid(), "Armaturer", 2, true, false)
        ]);

        var report = new JobReportSummaryResponse(
            jobId, Guid.NewGuid(), "Workslip Test", "12345678", "9319", JobStatus.Approved, null,
            new CustomerSnapshotResponse("Testkunde", "kunde@example.com", "12345678", "Testvej 1", "Kontaktperson"),
            "Testvej 1", "1234", "Testby", "Service",
            new JobReportSummaryWorkResponse(
                new JobWorkKindResponse(Guid.NewGuid(), "repair", "Reparationsarbejde", false, 1, null),
                [new InstallationTypeResponse(Guid.NewGuid(), "Gasinstallation", 1, [category])],
                [new JobReportSummaryClosureFlagResponse(Guid.NewGuid(), "done", "Færdig")],
                "Ventilen blev udskiftet, og installationen blev kontrolleret."),
            new JobReportSummaryObservationResponse("Udskift defekt ventil.", "Kunden er informeret.", "Ingen øvrige afvigelser."),
            [],
            [new JobLinkInfoResponse(Guid.NewGuid(), Guid.NewGuid(), "2862", "Relateret kunde", "Testvej 2", "Draft")],
            now.AddMonths(-1), now, now,
            [new AssignedUserResponse(Guid.NewGuid(), "Tekniker Test")],
            [new WorksheetResponse(Guid.NewGuid(), Guid.NewGuid(), jobId, Guid.NewGuid(), "Tekniker Test", new DateOnly(2026, 7, 8), 4.25m, false, now, now)],
            4.25m, 0, false, null);

        var pdf = new JobReportPdfService().Generate(report, JobStatus.Approved, new Uri("https://app.workslip.dk/jobs/"));

        Assert.True(pdf.Length > 1_000);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
    }
}
