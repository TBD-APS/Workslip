using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Workslip.Application.Jobs;
using Workslip.Application.Worksheets;
using Workslip.Domain;

namespace Workslip.Api.Services;

public interface IJobReportPdfService
{
    byte[] Generate(JobReportSummaryResponse job, JobStatus status, Uri? jobBaseUri = null);
}

public sealed class JobReportPdfService : IJobReportPdfService
{
    private static readonly CultureInfo DanishCulture = CultureInfo.GetCultureInfo("da-DK");

    private static class PdfStyle
    {
        public static readonly Color Primary = Color.FromHex("#06336B");
        public static readonly Color PrimaryDark = Color.FromHex("#022B5C");
        public static readonly Color PrimaryLight = Color.FromHex("#F4F7FB");
        public static readonly Color Accent = Color.FromHex("#1266A5");
        public static readonly Color Success = Color.FromHex("#278A43");
        public static readonly Color SuccessLight = Color.FromHex("#EFF8F1");
        public static readonly Color TextDark = Color.FromHex("#111827");
        public static readonly Color TextMedium = Color.FromHex("#4B5563");
        public static readonly Color TextLight = Color.FromHex("#6B7280");
        public static readonly Color BorderColor = Color.FromHex("#DDE3EA");
        public static readonly Color SectionBackground = Colors.White;
        public static readonly string FontFamily = "Helvetica";

        // Font Sizes
        public const int BaseFontSize = 10;
        public const int TitleSize = 16;
        public const int HeaderSize = 14;
        public const int SubHeaderSize = 12;
        public const int LabelSize = 9;
        public const int FieldValueSize = 10;
        public const int SectionTitleSize = 11;
        public const int SmallTextSize = 8;
        public const int MarkerSize = 10;

        // Spacing
        public const int PageMargin = 28;
        public const int SectionGap = 10;
        public const int GroupGap = 8;
        public const int ItemGap = 5;
        public const int CompactGap = 4;

        // Padding
        public const int SectionPadding = 12;
        public const int ContentPadding = 10;
        public const int CardPadding = 8;
        public const int ElementPadding = 6;
        public const int SmallPadding = 4;
    }

    public byte[] Generate(JobReportSummaryResponse job, JobStatus status, Uri? jobBaseUri = null)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                ConfigurePage(page);
                page.Header().Element(c => ComposeHeader(c, job, status));
                page.Content().PaddingTop(20).Element(c => ComposeOverviewPage(c, job, jobBaseUri));
                page.Footer().Element(c => ComposeFooter(c, "Servicerapport"));
            });

            container.Page(page =>
            {
                ConfigurePage(page);
                page.Header().Element(c => PageHeading(c, "KONTROLPUNKTER", "K"));
                page.Content().PaddingTop(16).Element(c => ComposeDetailsPage(c, job));
                page.Footer().Element(c => ComposeFooter(c, "Servicerapport"));
            });
        }).GeneratePdf();
    }

    private static void ConfigurePage(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.MarginHorizontal(PdfStyle.PageMargin);
        page.MarginTop(26);
        page.MarginBottom(0);
        page.DefaultTextStyle(x => x.FontSize(PdfStyle.BaseFontSize).FontFamily(PdfStyle.FontFamily).FontColor(PdfStyle.TextDark));
    }

    private static void ComposeHeader(IContainer container, JobReportSummaryResponse job, JobStatus status)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().AlignMiddle().Row(brand =>
                {
                    brand.ConstantItem(30).Height(30).Background(PdfStyle.Accent).CornerRadius(15)
                        .AlignCenter().AlignMiddle().Text("W").FontSize(14).Bold().FontColor(Colors.White);
                    brand.RelativeItem().PaddingLeft(8).AlignMiddle().Text("WORKSLIP").FontSize(20).Bold().FontColor(PdfStyle.Primary);
                });

                row.AutoItem().AlignRight().Column(info =>
                {
                    info.Item().AlignRight().Text("SERVICERAPPORT").FontSize(10).Bold().FontColor(PdfStyle.TextDark);
                    info.Item().AlignRight().Text("4V05-rapport").FontSize(9).FontColor(PdfStyle.TextMedium);
                });
            });
            col.Item().PaddingTop(14).LineHorizontal(1).LineColor(PdfStyle.BorderColor);
        });
    }

    private static void ComposeOverviewPage(IContainer container, JobReportSummaryResponse job, Uri? jobBaseUri)
    {
        container.Column(col =>
        {
            col.Spacing(PdfStyle.SectionGap);
            col.Item().Element(c => ComposeHero(c, job));
            col.Item().Row(row =>
            {
                row.RelativeItem().Element(c => ComposeCustomerSection(c, job));
                row.ConstantItem(12);
                row.RelativeItem().Element(c => ComposeMetaSection(c, job));
            });
            col.Item().Element(c => ComposeTaskSection(c, job));
            col.Item().Element(c => ComposeObservationsSection(c, job));
            col.Item().Element(c => ComposeLinksSection(c, job, jobBaseUri));    
        });
    }

    private static void ComposeDetailsPage(IContainer container, JobReportSummaryResponse job)
    {
        container.Column(col =>
        {
            col.Spacing(18);
            col.Item().Element(c => ComposeWorkSection(c, job));
            col.Item().Element(c => PageHeading(c, "ARBEJDSSEDLER", "A"));
            col.Item().Element(c => ComposeWorksheetsSection(c, job));
        });
    }

    private static void ComposeHero(IContainer container, JobReportSummaryResponse job)
    {
        container.PaddingVertical(4).Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text($"Sag #{job.ReportNumber ?? ShortId(job.Id)}").FontSize(25).Bold().FontColor(PdfStyle.TextDark);
                left.Item().PaddingTop(6).Text(WorkKindLabel(job.Work.WorkKind)).FontSize(12).FontColor(PdfStyle.TextMedium);
            });

            row.ConstantItem(175).Border(1).BorderColor(PdfStyle.BorderColor).CornerRadius(6).Padding(12).Column(card =>
            {
                card.Spacing(8);
                card.Item().Text(StatusLabel(job.Status)).FontSize(11).Bold().FontColor(StatusTextColor(job.Status));
                card.Item().Text("STATUS").FontSize(7).FontColor(PdfStyle.TextLight);
                card.Item().Text(FormatDate(GetReportDate(job))).FontSize(10).Bold();
                card.Item().Text("DATO").FontSize(7).FontColor(PdfStyle.TextLight);
            });
        });
    }

    private static DateOnly? GetReportDate(JobReportSummaryResponse job) =>
        job.SubmittedAt.HasValue ? DateOnly.FromDateTime(job.SubmittedAt.Value.LocalDateTime) : DateOnly.FromDateTime(job.UpdatedAt.LocalDateTime);

    private static void ComposeMetaSection(IContainer container, JobReportSummaryResponse job)
    {
        Section(container, "Sagsoplysninger", "S", body =>
        {
            body.Column(section =>
            {
                section.Spacing(PdfStyle.GroupGap);

                if (job.SoftDeleted)
                {
                    section.Item().Background(Color.FromHex("#FEF3C7")).Border(1).BorderColor(Color.FromHex("#F59E0B"))
                        .Padding(PdfStyle.ElementPadding).Text("Rapporten er markeret slettet.").FontSize(PdfStyle.LabelSize).FontColor(Color.FromHex("#92400E"));
                }

                section.Item().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Element(c => Field(c, "Sagsnummer", job.ReportNumber ?? "-"));
                        col.Item().Element(c => Field(c, "Organisation", Value(job.OrganizationName)));
                    });

                    row.ConstantItem(18);

                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Element(c => Field(c, "Oprettet", FormatDateTime(job.CreatedAt)));
                        col.Item().Element(c => Field(c, "Senest ændret", FormatDateTime(job.UpdatedAt)));
                    });
                section.Item().PaddingTop(PdfStyle.SmallPadding).Column(col =>
                {
                    if (job.AssignedUsers is not { Count: > 0 })
                    {
                        col.Item().Text("Ingen medarbejdere tildelt.").FontColor(PdfStyle.TextLight).Italic();
                        return;
                    }

                    col.Item().Text("Tildelte medarbejdere").FontSize(PdfStyle.LabelSize).FontColor(PdfStyle.TextLight);
                    foreach (var user in job.AssignedUsers.OrderBy(user => user.DisplayName))
                    {
                        col.Item().Element(c => Bullet(c, user.DisplayName));
                    }
                });

                section.Item().PaddingTop(PdfStyle.SmallPadding).Row(row =>
                {
                    row.RelativeItem().Element(c => Field(c, "Opgavetype", WorkKindLabel(job.Work.WorkKind)));
                    row.RelativeItem().Element(c => Field(c, "Afslutning", JoinLabels(job.Work.ClosureFlags.Select(flag => flag.Label))));
                });

                });
            });
        });
    }

    private static void ComposeCustomerSection(IContainer container, JobReportSummaryResponse job)
    {
        Section(container, "Kundeoplysninger", "K", body =>
        {
            body.Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Element(c => Field(c, "Navn", Value(job.CustomerSnapshot.Name)));
                    col.Item().Element(c => Field(c, "Adresse", Value(job.CustomerSnapshot.Address)));
                    col.Item().Element(c => Field(c, "Kontaktperson", Value(job.CustomerSnapshot.ContactPerson)));
                });

                row.ConstantItem(18);

                row.RelativeItem().Column(col =>
                {
                    col.Item().Element(c => Field(c, "Email", Value(job.CustomerSnapshot.Email)));
                    col.Item().Element(c => Field(c, "Telefon", Value(job.CustomerSnapshot.Phone)));
                });
            });
        });
    }

    private static void ComposeWorkSection(IContainer container, JobReportSummaryResponse job)
    {
        container.Element(body =>
        {
            body.Column(col =>
            {
                if (job.Work.InstallationTypes is not { Count: > 0 })
                {
                    col.Item().Text("Ingen anlægstyper valgt.").FontColor(PdfStyle.TextLight).Italic();
                    return;
                }

                foreach (var installation in job.Work.InstallationTypes.OrderBy(x => x.SortOrder))
                {
                    col.Item().Element(c => ComposeInstallation(c, installation));
                }
            });
        });
    }

    private static void ComposeInstallation(IContainer container, InstallationTypeResponse installation)
    {
        container.Border(1).BorderColor(PdfStyle.BorderColor).CornerRadius(5).Background(PdfStyle.SectionBackground).Column(col =>
        {
            col.Item().Background(PdfStyle.Primary).PaddingVertical(10).PaddingHorizontal(14)
                .Text(installation.Name).FontSize(12).Bold().FontColor(Colors.White);

            foreach (var category in installation.Categories.OrderBy(x => x.IsIrrelevant).ThenBy(x => x.SortOrder))
            {
                col.Item().PaddingHorizontal(12).PaddingVertical(7).Element(c => ComposeCategory(c, category));
            }
        });
    }

    private static void ComposeCategory(IContainer container, InstallationTypeCategoryResponse category)
    {
        container.Column(col =>
        {
            col.Spacing(PdfStyle.CompactGap);
            col.Item().BorderBottom(1).BorderColor(PdfStyle.PrimaryLight).PaddingBottom(PdfStyle.SmallPadding).Row(row =>
            {
                row.RelativeItem().Text(category.Name).FontSize(11).SemiBold().FontColor(category.IsIrrelevant ? PdfStyle.TextLight : PdfStyle.TextDark);
                if (category.IsIrrelevant)
                {
                    row.AutoItem().Text("Irrelevant").FontSize(10).FontColor(Color.FromHex("#B45309"));
                }
            });

            if (category.IsIrrelevant)
            {
                col.Item().PaddingTop(4).Background(PdfStyle.PrimaryLight).Padding(8)
                    .Text("Ikke relevant").FontSize(PdfStyle.LabelSize).FontColor(PdfStyle.TextMedium);
                return;
            }

            if (category.ControlPoints is not { Count: > 0 })
            {
                col.Item().Text("Ingen kontrolpunkter valgt.").FontSize(10).FontColor(PdfStyle.TextLight).Italic();
                return;
            }

            foreach (var controlPoint in category.ControlPoints.OrderBy(x => x.SortOrder))
            {
                col.Item().Element(c => ControlPointRow(c, controlPoint));
            }
        });
    }

    private static void ControlPointRow(IContainer container, InstallationTypeControlPointResponse controlPoint)
    {
        container.Border(1).BorderColor(PdfStyle.BorderColor).CornerRadius(4).PaddingVertical(7).PaddingHorizontal(9).Row(row =>
        {
            row.ConstantItem(18).Height(18)
                .Background(controlPoint.IsChecked ? PdfStyle.Success : Colors.White)
                .Border(1).BorderColor(controlPoint.IsChecked ? PdfStyle.Success : PdfStyle.BorderColor)
                .AlignCenter().AlignMiddle().Text(controlPoint.IsChecked ? "X" : "").FontSize(8).Bold().FontColor(Colors.White);
            row.RelativeItem().PaddingLeft(10).AlignMiddle().Text(controlPoint.Name).FontSize(10).FontColor(PdfStyle.TextDark);
            row.AutoItem().PaddingLeft(8).AlignMiddle()
                .Text(controlPoint.IsChecked ? "Godkendt" : "Ikke kontrolleret")
                .FontSize(PdfStyle.SmallTextSize)
                .FontColor(controlPoint.IsChecked ? PdfStyle.Success : PdfStyle.TextMedium);
        });
    }

    private static void ComposeObservationsSection(IContainer container, JobReportSummaryResponse job)
    {
        Section(container, "Observationer", "O", body =>
        {
            body.Row(row =>
            {
                row.RelativeItem().Element(c => LongText(c, "Tekniske observationer", job.Observations.TechnicalObservations));
                row.ConstantItem(12);
                row.RelativeItem().Element(c => LongText(c, "Information til kunden", job.Observations.CustomerObservations));
            });
        });
    }

    private static void ComposeTaskSection(IContainer container, JobReportSummaryResponse job)
    {
        container.Column(col =>
        {
            col.Item().Element(c => PageHeading(c, "OPGAVEBESKRIVELSE", "B"));
            col.Item().PaddingTop(8).Text(Value(job.Observations.TaskDescription)).FontSize(PdfStyle.FieldValueSize);
            col.Item().PaddingTop(16).Element(c => PageHeading(c, "UDFØRT ARBEJDE", "U"));
            col.Item().PaddingTop(8).Background(PdfStyle.PrimaryLight).Padding(10)
                .Text(HasValue(job.Work.Remarks)
                    ? job.Work.Remarks!
                    : "Se kontrolpunkter og arbejdssedler for udført arbejde.")
                .FontSize(PdfStyle.FieldValueSize).FontColor(PdfStyle.TextMedium);
        });
    }

    private static void ComposeWorksheetsSection(IContainer container, JobReportSummaryResponse job)
    {
        container.Element(body =>
        {
            body.Column(col =>
            {
                if (job.Worksheets.Count == 0)
                {
                    col.Item().Text("Ingen arbejdssedler registreret.").FontColor(PdfStyle.TextLight).Italic();
                    return;
                }

                var worksheets = job.Worksheets.OrderBy(x => x.WorkDate).ThenBy(x => x.UserDisplayName).ToList();

                var userGroups = worksheets
                    .GroupBy(x => x.UserDisplayName ?? "Ukendt")
                    .OrderBy(g => g.Key)
                    .ToList();

                col.Spacing(PdfStyle.CompactGap);
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(4);
                        columns.RelativeColumn(2);
                        columns.ConstantColumn(55);
                        columns.ConstantColumn(65);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(c => TableHeaderCell(c, "Medarbejder"));
                        header.Cell().Element(c => TableHeaderCell(c, "Dato"));
                        header.Cell().Element(c => TableHeaderCell(c, "Timer"));
                        header.Cell().Element(c => TableHeaderCell(c, "Overnatning"));
                    });

                    var rowIndex = 0;
                    foreach (var group in userGroups)
                    {
                        foreach (var ws in group)
                        {
                            var bgColor = (rowIndex % 2 == 0) ? Colors.White : PdfStyle.PrimaryLight;

                            table.Cell().Element(c => TableCell(c, ws.UserDisplayName ?? "-", bgColor));
                            table.Cell().Element(c => TableCell(c, FormatDate(ws.WorkDate), bgColor));
                            table.Cell().Element(c => TableCell(c, FormatDecimal(ws.HoursWorked), bgColor));
                            table.Cell().Element(c => TableCell(c, ws.SleptOnJob ? "Ja" : "Nej", bgColor));
                            rowIndex++;
                        }

                        var userTotalHours = group.Sum(x => x.HoursWorked);
                        var userOutlayCount = group.Count(x => x.SleptOnJob);

                        table.Cell().Element(c => SubtotalLabelCell(c, $"{group.Key} - i alt"));
                        table.Cell().Element(c => SubtotalValueCell(c, "-"));
                        table.Cell().Element(c => SubtotalValueCell(c, FormatDecimal(userTotalHours)));
                        table.Cell().Element(c => SubtotalValueCell(c, FormatOvernightStays(userOutlayCount)));
                        rowIndex++;
                    }

                    table.Cell().Element(c => TotalLabelCell(c, "I alt"));
                    table.Cell().Element(c => TotalValueCell(c, "-"));
                    table.Cell().Element(c => TotalValueCell(c, FormatDecimal(job.TotalHours)));
                    table.Cell().Element(c => TotalValueCell(c, FormatOvernightStays(job.TotalOutlay)));
                });
            });
        });
    }

    private static void ComposeLinksSection(IContainer container, JobReportSummaryResponse job, Uri? jobBaseUri)
    {
        Section(container, "Relaterede sager", "R", body =>
        {
            if (job.Links.Count == 0)
            {
                body.Text("Ingen relaterede sager.").FontColor(PdfStyle.TextLight).Italic();
                return;
            }

            body.Column(col =>
            {
                col.Spacing(PdfStyle.CompactGap);
                foreach (var link in job.Links.OrderBy(link => link.LinkedReportNumber))
                {
                    col.Item().Element(c => ComposeLink(c, link, jobBaseUri));
                }
            });
        });
    }

    private static void ComposeLink(IContainer container, JobLinkInfoResponse link, Uri? jobBaseUri)
    {
        var linkContainer = jobBaseUri is null
            ? container
            : container.Hyperlink(new Uri(jobBaseUri, link.LinkedReportId.ToString()).ToString());

        linkContainer.Border(1).BorderColor(PdfStyle.BorderColor).Padding(PdfStyle.ElementPadding).Text(text =>
        {
            text.Span($"{link.LinkedReportNumber} · {link.LinkedCustomerName}").FontSize(11).FontColor(PdfStyle.Accent).Underline();
            text.Span($" ({ParseStatusLabel(link.LinkedStatus)})").FontSize(10).FontColor(PdfStyle.TextMedium);
        });
    }

    private static void Section(IContainer container, string title, string marker, Action<IContainer> compose)
    {
        container.Border(1).BorderColor(PdfStyle.BorderColor).CornerRadius(5).Padding(12).Column(col =>
        {
            col.Item().Element(c => PageHeading(c, title.ToUpperInvariant(), marker));
            col.Item().PaddingTop(10).Element(c => compose(c));
        });
    }

    private static void PageHeading(IContainer container, string title, string marker)
    {
        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.ConstantItem(24).Height(24).Border(1).BorderColor(PdfStyle.Accent).CornerRadius(12)
                    .AlignCenter().AlignMiddle().Text(marker).FontSize(9).Bold().FontColor(PdfStyle.Accent);
                row.RelativeItem().PaddingLeft(8).AlignMiddle().Text(title).FontSize(PdfStyle.SectionTitleSize).Bold().FontColor(PdfStyle.Primary);
            });
            col.Item().PaddingTop(7).LineHorizontal(1).LineColor(PdfStyle.BorderColor);
        });
    }

    private static void Field(IContainer container, string label, string value)
    {
        container.PaddingVertical(3).Column(col =>
        {
            col.Item().Text(label).FontSize(PdfStyle.LabelSize).FontColor(PdfStyle.TextLight);
            col.Item().Text(value).FontSize(PdfStyle.FieldValueSize).FontColor(PdfStyle.TextDark);
        });
    }

    private static void LongText(IContainer container, string label, string? value)
    {
        if (!HasValue(value))
        {
            Field(container, label, "-");
            return;
        }

        container.Column(col =>
        {
            col.Item().Text(label).FontSize(PdfStyle.LabelSize).FontColor(PdfStyle.TextLight);
            col.Item().Text(value!).FontSize(PdfStyle.FieldValueSize).FontColor(PdfStyle.TextDark);
        });
    }

    private static void Bullet(IContainer container, string title)
    {
        container.Row(row =>
        {
            row.ConstantItem(12).Text("•").FontSize(10).FontColor(PdfStyle.Accent);
            row.RelativeItem().Text(title).FontSize(PdfStyle.FieldValueSize).FontColor(PdfStyle.TextDark);
        });
    }

    private static void TotalLabelCell(IContainer container, string text)
    {
        container.Background(PdfStyle.PrimaryLight)
            .BorderTop(2).BorderBottom(1).BorderColor(PdfStyle.Primary)
            .DefaultTextStyle(x => x.FontSize(PdfStyle.FieldValueSize).FontColor(PdfStyle.Primary).SemiBold())
            .PaddingVertical(3).PaddingHorizontal(PdfStyle.SmallPadding)
            .Text(text);
    }

    private static void TotalValueCell(IContainer container, string text)
    {
        container.Background(PdfStyle.PrimaryLight)
            .BorderTop(2).BorderBottom(1).BorderColor(PdfStyle.Primary)
            .DefaultTextStyle(x => x.FontSize(PdfStyle.FieldValueSize).FontColor(PdfStyle.Primary).Bold())
            .PaddingVertical(3).PaddingHorizontal(PdfStyle.SmallPadding)
            .Text(text);
    }

    private static void TableHeaderCell(IContainer container, string text)
    {
        container.DefaultTextStyle(x => x.SemiBold().FontSize(PdfStyle.LabelSize).FontColor(PdfStyle.TextMedium))
            .PaddingVertical(PdfStyle.SmallPadding).PaddingHorizontal(PdfStyle.SmallPadding)
            .BorderBottom(1).BorderColor(PdfStyle.BorderColor)
            .Text(text);
    }

    private static void TableCell(IContainer container, string text, Color background)
    {
        container.Background(background)
            .DefaultTextStyle(x => x.FontSize(PdfStyle.FieldValueSize).FontColor(PdfStyle.TextDark))
            .PaddingVertical(3).PaddingHorizontal(PdfStyle.SmallPadding)
            .Text(text);
    }

    private static void SubtotalLabelCell(IContainer container, string text)
    {
        container.Background(PdfStyle.PrimaryLight)
            .BorderTop(1).BorderBottom(1).BorderColor(PdfStyle.Primary)
            .DefaultTextStyle(x => x.FontSize(PdfStyle.FieldValueSize).FontColor(PdfStyle.Primary).SemiBold())
            .PaddingVertical(3).PaddingHorizontal(PdfStyle.SmallPadding)
            .Text(text);
    }

    private static void SubtotalValueCell(IContainer container, string text)
    {
        container.Background(PdfStyle.PrimaryLight)
            .BorderTop(1).BorderBottom(1).BorderColor(PdfStyle.Primary)
            .DefaultTextStyle(x => x.FontSize(PdfStyle.FieldValueSize).FontColor(PdfStyle.Primary).Bold())
            .PaddingVertical(3).PaddingHorizontal(PdfStyle.SmallPadding)
            .Text(text);
    }

    private static void ComposeFooter(IContainer container, string reportType)
    {
        container.Background(PdfStyle.PrimaryDark).PaddingVertical(13).PaddingHorizontal(10).Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text("WORKSLIP").FontSize(8).Bold().FontColor(Colors.White);
                left.Item().Text(reportType).FontSize(8).FontColor(Color.FromHex("#D8E5F5"));
            });
            row.RelativeItem().AlignCenter().Text($"Genereret {FormatDateTime(DateTimeOffset.Now)}")
                .FontSize(8).FontColor(Color.FromHex("#D8E5F5"));
            row.RelativeItem().AlignRight().DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.White)).Text(text =>
            {
                text.Span("Side ");
                text.CurrentPageNumber();
                text.Span(" af ");
                text.TotalPages();
            });
        });
    }

    private static string WorkKindLabel(JobWorkKindResponse? workKind)
    {
        if (workKind is null) return "-";
        return HasValue(workKind.CustomWorkKind)
            ? $"{workKind.Label} - {workKind.CustomWorkKind}"
            : workKind.Label;
    }

    private static string JoinLabels(IEnumerable<string?> values)
    {
        var labels = values.Where(HasValue).Select(value => value!.Trim()).ToArray();
        return labels.Length == 0 ? "-" : string.Join(", ", labels);
    }

    private static string Value(string? value) => HasValue(value) ? value!.Trim() : "-";

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);

    private static string ShortId(Guid id) => id.ToString("N")[..8].ToUpperInvariant();

    private static string FormatDate(DateOnly? value) => value.HasValue ? CapitalizeDanishDate(value.Value.ToString("d. MMMM yyyy", DanishCulture)) : "-";

    private static string FormatDate(DateOnly value) => CapitalizeDanishDate(value.ToString("d. MMMM yyyy", DanishCulture));

    private static string FormatDateTime(DateTimeOffset? value) =>
        value.HasValue ? CapitalizeDanishDate(value.Value.ToLocalTime().ToString("d. MMMM yyyy HH:mm", DanishCulture)) : "-";

    private static string FormatDateTime(DateTimeOffset value) =>
        CapitalizeDanishDate(value.ToLocalTime().ToString("d. MMMM yyyy HH:mm", DanishCulture));

    private static string CapitalizeDanishDate(string date)
    {
        var parts = date.Split(' ', 2);
        if (parts.Length < 2) return date;
        return $"{parts[0]} {char.ToUpper(parts[1][0])}{parts[1].Substring(1)}";
    }

    private static string FormatDecimal(decimal? value) =>
        value.HasValue ? value.Value.ToString("0.##", DanishCulture) : "-";

    private static string FormatOvernightStays(int? value) =>
        value.HasValue ? $"{value.Value} {(value.Value == 1 ? "nat" : "nætter")}" : "-";

    private static string StatusLabel(JobStatus status) => status switch
    {
        JobStatus.Draft => "Aktiv",
        JobStatus.InReview => "Til gennemsyn",
        JobStatus.Approved => "Godkendt",
        JobStatus.Rejected => "Returneret",
        _ => status.ToString()
    };

    private static string ParseStatusLabel(string status) => status switch
    {
        "Draft" => "Aktiv",
        "InReview" => "Til gennemsyn",
        "Approved" => "Godkendt",
        "Rejected" => "Returneret",
        _ => status
    };

    private static Color StatusColor(JobStatus status) => status switch
    {
        JobStatus.Draft => Color.FromHex("#CBD5E1"),
        JobStatus.InReview => Color.FromHex("#93C5FD"),
        JobStatus.Approved => Color.FromHex("#86EFAC"),
        JobStatus.Rejected => Color.FromHex("#FCA5A5"),
        _ => Colors.White
    };

    private static Color StatusTextColor(JobStatus status) => status switch
    {
        JobStatus.Approved => PdfStyle.Success,
        JobStatus.Rejected => Color.FromHex("#B42318"),
        JobStatus.InReview => PdfStyle.Accent,
        _ => PdfStyle.TextMedium
    };
}
