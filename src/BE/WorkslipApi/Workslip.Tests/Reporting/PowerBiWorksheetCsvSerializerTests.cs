using System.Text;
using Workslip.Application.Worksheets;
using Workslip.Infrastructure.Reporting;

namespace Workslip.Tests.Reporting;

public sealed class PowerBiWorksheetCsvSerializerTests
{
    [Fact]
    public void Serialize_UsesStableMinimizedSchemaAndInvariantValues()
    {
        var exportedAt = new DateTimeOffset(2026, 8, 14, 20, 0, 0, TimeSpan.Zero);
        var entry = new MyWorksheetEntryResponse(
            new DateOnly(2026, 8, 14),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "SAG-0042",
            "Kunde Nord",
            "Should not be exported",
            7.5m,
            true,
            "Montør A",
            "Service",
            950m,
            7125m);

        var csv = Encoding.UTF8.GetString(PowerBiWorksheetCsvSerializer.Serialize([entry], exportedAt));

        Assert.Contains(
            "WorkDate,Year,Month,IsoWeek,ReportNumber,CustomerName,Employee,JobType,HoursWorked,HasOutlay,BillableHourlyRate,BillableAmount,ExportedAtUtc",
            csv);
        Assert.Contains("2026-08-14,2026,8,33,SAG-0042,Kunde Nord,Montør A,Service,7.5,True,950,7125", csv);
        Assert.DoesNotContain("Should not be exported", csv);
        Assert.DoesNotContain(entry.JobId.ToString(), csv);
        Assert.DoesNotContain(entry.UserId.ToString(), csv);
    }

    [Fact]
    public void Serialize_NeutralizesSpreadsheetFormulaPrefixes()
    {
        var entry = new MyWorksheetEntryResponse(
            new DateOnly(2026, 8, 14),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "=CMD()",
            "+Kunde",
            null,
            1m,
            false,
            "@Employee",
            "-Type");

        var csv = Encoding.UTF8.GetString(PowerBiWorksheetCsvSerializer.Serialize(
            [entry],
            DateTimeOffset.UtcNow));

        Assert.Contains("'=CMD()", csv);
        Assert.Contains("'+Kunde", csv);
        Assert.Contains("'@Employee", csv);
        Assert.Contains("'-Type", csv);
    }
}
