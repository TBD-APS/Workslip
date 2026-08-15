using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Workslip.Application.Worksheets;

namespace Workslip.Infrastructure.Reporting;

public static class PowerBiWorksheetCsvSerializer
{
    public static byte[] Serialize(
        IEnumerable<MyWorksheetEntryResponse> entries,
        DateTimeOffset exportedAtUtc)
    {
        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(
                   stream,
                   new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
                   bufferSize: 1024,
                   leaveOpen: true))
        using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
               {
                   NewLine = "\r\n"
               }))
        {
            var rows = entries
                .OrderBy(entry => entry.WorkDate)
                .ThenBy(entry => entry.UserDisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.ReportNumber, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new PowerBiWorksheetExportRow(
                    entry.WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    entry.WorkDate.Year,
                    entry.WorkDate.Month,
                    ISOWeek.GetWeekOfYear(entry.WorkDate.ToDateTime(TimeOnly.MinValue)),
                    SafeText(entry.ReportNumber) ?? string.Empty,
                    SafeText(entry.CustomerName) ?? string.Empty,
                    SafeText(entry.UserDisplayName),
                    SafeText(entry.JobType),
                    entry.HoursWorked,
                    entry.HasOutlay,
                    entry.BillableHourlyRate,
                    entry.BillableAmount,
                    exportedAtUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)))
                .ToArray();

            csv.WriteRecords(rows);
        }

        return stream.ToArray();
    }

    private static string? SafeText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        return trimmed[0] is '=' or '+' or '-' or '@'
            ? $"'{trimmed}"
            : trimmed;
    }

    private sealed record PowerBiWorksheetExportRow(
        string WorkDate,
        int Year,
        int Month,
        int IsoWeek,
        string ReportNumber,
        string CustomerName,
        string? Employee,
        string? JobType,
        decimal HoursWorked,
        bool HasOutlay,
        decimal? BillableHourlyRate,
        decimal? BillableAmount,
        string ExportedAtUtc);
}
