using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Workslip.Application.Worksheets;

namespace Workslip.Infrastructure;

public sealed class MonthlyHoursPdfGenerator : IMonthlyHoursPdfGenerator
{
    private static readonly CultureInfo DanishCulture = CultureInfo.GetCultureInfo("da-DK");
    private static readonly Color Primary = Color.FromHex("#334155");
    private static readonly Color LightBackground = Color.FromHex("#F8FAFC");
    private static readonly Color Border = Color.FromHex("#CBD5E1");
    private static readonly Color Muted = Color.FromHex("#64748B");
    private static readonly ImageGenerationSettings PreviewImageSettings = new()
    {
        ImageFormat = ImageFormat.Png,
        ImageCompressionQuality = ImageCompressionQuality.High,
        RasterDpi = 144
    };

    public byte[] Generate(MyWorksheetsMonthResponse month) =>
        CreateDocument(month).GeneratePdf();

    public IReadOnlyList<byte[]> GeneratePreviewPages(MyWorksheetsMonthResponse month) =>
        CreateDocument(month).GenerateImages(PreviewImageSettings).ToArray();

    private static IDocument CreateDocument(MyWorksheetsMonthResponse month)
    {
        var rows = Flatten(month);
        var employees = rows
            .GroupBy(row => row.UserId)
            .Select(group => new EmployeeSummary(
                group.Key,
                group.Select(row => row.EmployeeName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "Ukendt medarbejder",
                group.OrderBy(row => row.WorkDate).ThenBy(row => row.ReportNumber).ToArray()))
            .OrderBy(employee => employee.Name, StringComparer.Create(DanishCulture, ignoreCase: true))
            .ThenBy(employee => employee.UserId)
            .ToArray();
        var weeks = rows.Select(row => row.Week).Distinct().OrderBy(week => week).ToArray();

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(28);
                page.DefaultTextStyle(style => style.FontFamily("Helvetica").FontSize(9));
                page.Header().Element(container => ComposeHeader(container, month));
                page.Content().PaddingTop(12).Column(column =>
                {
                    column.Spacing(12);
                    column.Item().Element(container => ComposeKpis(container, rows, employees));
                    column.Item().Element(container => ComposeSummary(container, rows, employees, weeks));
                    column.Item().PageBreak();
                    column.Item().Text("Detaljer").FontSize(13).Bold().FontColor(Primary);
                    foreach (var employee in employees)
                    {
                        column.Item().Element(container => ComposeEmployeeDetails(container, employee));
                    }
                });
                page.Footer().AlignCenter().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(8).FontColor(Muted));
                    text.Span("Side ");
                    text.CurrentPageNumber();
                    text.Span(" af ");
                    text.TotalPages();
                });
            });
        });
    }

    private static void ComposeHeader(IContainer container, MyWorksheetsMonthResponse month)
    {
        container.BorderBottom(1.5f).BorderColor(Primary).PaddingBottom(8).Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("WORKSLIP").FontSize(10).Bold().FontColor(Primary).LetterSpacing(0.12f);
                column.Item().Text($"Timeoversigt · {MonthLabel(month.Year, month.Month)}").FontSize(18).Bold().FontColor(Primary);
                column.Item().Text($"{FormatDate(month.MonthStart)} – {FormatDate(month.MonthEnd)}").FontSize(9).FontColor(Muted);
            });
            row.AutoItem().AlignBottom().Text($"Genereret {DateTimeOffset.Now.ToString("dd.MM.yyyy HH:mm", DanishCulture)}")
                .FontSize(8).FontColor(Muted);
        });
    }

    private static void ComposeKpis(IContainer container, IReadOnlyList<HoursRow> rows, IReadOnlyList<EmployeeSummary> employees)
    {
        var totalHours = rows.Sum(row => row.Hours);
        container.Row(row =>
        {
            row.RelativeItem().Element(cell => Kpi(cell, "Timer i alt", $"{FormatHours(totalHours)} t"));
            row.ConstantItem(8);
            row.RelativeItem().Element(cell => Kpi(cell, "Medarbejdere", employees.Count.ToString(DanishCulture)));
            row.ConstantItem(8);
            row.RelativeItem().Element(cell => Kpi(cell, "Registreringer", rows.Count.ToString(DanishCulture)));
        });
    }

    private static void Kpi(IContainer container, string label, string value)
    {
        container.Border(1).BorderColor(Border).Background(LightBackground).Padding(10).Column(column =>
        {
            column.Item().Text(label.ToUpper(DanishCulture)).FontSize(7).FontColor(Muted);
            column.Item().Text(value).FontSize(14).Bold().FontColor(Primary);
        });
    }

    private static void ComposeSummary(
        IContainer container,
        IReadOnlyList<HoursRow> rows,
        IReadOnlyList<EmployeeSummary> employees,
        IReadOnlyList<int> weeks)
    {
        container.Column(column =>
        {
            column.Item().PaddingBottom(5).Text("Overblik pr. medarbejder").FontSize(13).Bold().FontColor(Primary);
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3);
                    foreach (var _ in weeks)
                        columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().Element(TableHeader).Text("Medarbejder");
                    foreach (var week in weeks)
                        header.Cell().Element(TableHeader).AlignRight().Text($"Uge {week}");
                    header.Cell().Element(TableHeader).AlignRight().Text("I alt");
                });

                foreach (var employee in employees)
                {
                    table.Cell().Element(TableCell).Text(employee.Name);
                    foreach (var week in weeks)
                    {
                        var hours = employee.Rows.Where(row => row.Week == week).Sum(row => row.Hours);
                        table.Cell().Element(TableCell).AlignRight().Text(FormatHours(hours));
                    }
                    table.Cell().Element(TableCell).AlignRight().Text(FormatHours(employee.Rows.Sum(row => row.Hours))).Bold();
                }

                table.Cell().Element(TotalCell).Text("I alt").Bold();
                foreach (var week in weeks)
                {
                    var hours = rows.Where(row => row.Week == week).Sum(row => row.Hours);
                    table.Cell().Element(TotalCell).AlignRight().Text(FormatHours(hours)).Bold();
                }
                table.Cell().Element(TotalCell).AlignRight().Text(FormatHours(rows.Sum(row => row.Hours))).Bold();
            });
        });
    }

    private static void ComposeEmployeeDetails(IContainer container, EmployeeSummary employee)
    {
        container.PaddingBottom(12).Column(column =>
        {
            column.Item().BorderBottom(1).BorderColor(Border).PaddingBottom(4).Row(row =>
            {
                row.RelativeItem().Text(employee.Name).FontSize(11).Bold().FontColor(Primary);
                row.AutoItem().Text($"{FormatHours(employee.Rows.Sum(item => item.Hours))} timer").Bold().FontColor(Primary);
            });

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(65);
                    columns.ConstantColumn(40);
                    columns.RelativeColumn(1.3f);
                    columns.RelativeColumn(3);
                    columns.ConstantColumn(55);
                });

                table.Header(header =>
                {
                    header.Cell().Element(TableHeader).Text("Dato");
                    header.Cell().Element(TableHeader).AlignCenter().Text("Uge");
                    header.Cell().Element(TableHeader).Text("Sag");
                    header.Cell().Element(TableHeader).Text("Kunde");
                    header.Cell().Element(TableHeader).AlignRight().Text("Timer");
                });

                foreach (var row in employee.Rows)
                {
                    table.Cell().Element(TableCell).Text(FormatDate(row.WorkDate));
                    table.Cell().Element(TableCell).AlignCenter().Text(row.Week.ToString(DanishCulture));
                    table.Cell().Element(TableCell).Text(row.ReportNumber);
                    table.Cell().Element(TableCell).Text(row.CustomerName);
                    table.Cell().Element(TableCell).AlignRight().Text(FormatHours(row.Hours));
                }
            });
        });
    }

    private static IContainer TableHeader(IContainer container) =>
        container.Background(Color.FromHex("#E2E8F0")).BorderBottom(1).BorderColor(Border).PaddingVertical(5).PaddingHorizontal(6);

    private static IContainer TableCell(IContainer container) =>
        container.BorderBottom(0.5f).BorderColor(Border).PaddingVertical(5).PaddingHorizontal(6);

    private static IContainer TotalCell(IContainer container) =>
        container.Background(LightBackground).BorderTop(1).BorderColor(Primary).PaddingVertical(6).PaddingHorizontal(6);

    private static HoursRow[] Flatten(MyWorksheetsMonthResponse month) =>
        month.Weeks
            .SelectMany(week => week.Days)
            .SelectMany(day => day.Entries)
            .Where(entry => entry.WorkDate >= month.MonthStart && entry.WorkDate <= month.MonthEnd)
            .Select(entry => new HoursRow(
                entry.WorkDate,
                ISOWeek.GetWeekOfYear(entry.WorkDate.ToDateTime(TimeOnly.MinValue)),
                entry.UserId,
                string.IsNullOrWhiteSpace(entry.UserDisplayName) ? "Ukendt medarbejder" : entry.UserDisplayName.Trim(),
                string.IsNullOrWhiteSpace(entry.ReportNumber) ? "—" : entry.ReportNumber.Trim(),
                string.IsNullOrWhiteSpace(entry.CustomerName) ? "Ukendt kunde" : entry.CustomerName.Trim(),
                entry.HoursWorked))
            .OrderBy(row => row.EmployeeName, StringComparer.Create(DanishCulture, ignoreCase: true))
            .ThenBy(row => row.UserId)
            .ThenBy(row => row.WorkDate)
            .ThenBy(row => row.ReportNumber, StringComparer.Create(DanishCulture, ignoreCase: true))
            .ToArray();

    private static string FormatHours(decimal value) => value.ToString("0.##", DanishCulture);
    private static string FormatDate(DateOnly value) => value.ToString("dd.MM.yyyy", DanishCulture);
    private static string MonthLabel(int year, int month) =>
        new DateTime(year, month, 1).ToString("MMMM yyyy", DanishCulture);

    private sealed record HoursRow(
        DateOnly WorkDate,
        int Week,
        Guid UserId,
        string EmployeeName,
        string ReportNumber,
        string CustomerName,
        decimal Hours);

    private sealed record EmployeeSummary(Guid UserId, string Name, IReadOnlyList<HoursRow> Rows);
}
