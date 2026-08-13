using System.Text;
using QuestPDF.Infrastructure;
using Workslip.Application.Worksheets;
using Workslip.Infrastructure;
using Xunit;

namespace Workslip.Tests.Worksheets;

public sealed class MonthlyCostingPdfGeneratorTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void Generator_renders_costing_pdf_and_preview()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var userId = Guid.NewGuid();
        var workDate = new DateOnly(2026, 8, 14);
        var entry = new MyWorksheetEntryResponse(
            workDate,
            Guid.NewGuid(),
            userId,
            "R-428",
            "Kunde",
            null,
            3.5m,
            false,
            "Medarbejder",
            null,
            725m,
            2537.50m);
        var day = new MyWorksheetDayResponse(workDate, 3.5m, 0, [entry]);
        var month = new MyWorksheetsMonthResponse(
            2026,
            8,
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 31),
            3.5m,
            0,
            [new MyWorksheetWeekResponse(
                new DateOnly(2026, 8, 10),
                new DateOnly(2026, 8, 16),
                3.5m,
                0,
                [day])]);
        var generator = new MonthlyCostingPdfGenerator();

        var pdf = generator.Generate(month);
        var previewPages = generator.GeneratePreviewPages(month);

        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
        Assert.NotEmpty(previewPages);
        Assert.All(previewPages, page => Assert.True(page.AsSpan().StartsWith(PngSignature)));
    }
}
