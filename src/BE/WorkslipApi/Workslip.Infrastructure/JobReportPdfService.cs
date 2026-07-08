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
        public static readonly Color Primary = Color.FromHex("#334155");
        public static readonly Color PrimaryLight = Color.FromHex("#F1F5F9");
        public static readonly Color Accent = Color.FromHex("#64748B");
        public static readonly Color TextDark = Color.FromHex("#0F172A");
        public static readonly Color TextMedium = Color.FromHex("#475569");
        public static readonly Color TextLight = Color.FromHex("#94A3B8");
        public static readonly Color BorderColor = Color.FromHex("#E2E8F0");
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
        public const int PageMargin = 36;
        public const int SectionGap = 8;
        public const int GroupGap = 6;
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
                page.Size(PageSizes.A4);
                page.Margin(PdfStyle.PageMargin);
                page.DefaultTextStyle(x => x.FontSize(PdfStyle.BaseFontSize).FontFamily(PdfStyle.FontFamily));

                page.Header().Element(c => ComposeHeader(c, job, status));
                page.Content().PaddingTop(PdfStyle.ContentPadding).Element(c => ComposeContent(c, job, jobBaseUri));
                page.Footer().Element(c => ComposeFooter(c));
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, JobReportSummaryResponse job, JobStatus status)
    {
        container.Column(col =>
        {
            col.Item().Background(PdfStyle.Primary).Padding(PdfStyle.SectionPadding).Row(row =>
            {
                row.RelativeItem().Column(brand =>
                {
                    brand.Item().Text("WORKSLIP").FontSize(PdfStyle.TitleSize).Bold().FontColor(Colors.White);
                    brand.Item().Text("4V05-rapport").FontSize(PdfStyle.SubHeaderSize).FontColor(Color.FromHex("#CBD5E1"));
                });

                row.RelativeItem().AlignRight().Column(info =>
                {
                    info.Item().Text(job.ReportNumber ?? ShortId(job.Id)).FontSize(PdfStyle.HeaderSize).Bold().FontColor(Colors.White);
                    info.Item().PaddingTop(PdfStyle.SmallPadding).Border(1).BorderColor(StatusColor(status)).Background(StatusColor(status)).PaddingHorizontal(PdfStyle.CardPadding).PaddingVertical(2).AlignCenter().Text(StatusLabel(status)).FontSize(PdfStyle.SmallTextSize).Bold().FontColor(PdfStyle.TextDark);
                });
            });

        });
    }

    private static void ComposeContent(IContainer container, JobReportSummaryResponse job, Uri? jobBaseUri)
    {
        container.Column(col =>
        {
            col.Spacing(PdfStyle.SectionGap);
            col.Item().Element(c => ComposeMetaSection(c, job));
            col.Item().Element(c => ComposeCustomerSection(c, job));
            col.Item().Element(c => ComposeObservationsSection(c, job));
            col.Item().Element(c => ComposeLinksSection(c, job, jobBaseUri));    
            col.Item().Element(c => ComposeWorkSection(c, job));
            col.Item().Element(c => ComposeWorksheetsSection(c, job));
        });
    }

    private static void ComposeMetaSection(IContainer container, JobReportSummaryResponse job)
    {
        Section(container, "Sag", body =>
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

                if (HasValue(job.Work.Remarks))
                {
                    section.Item().Element(c => Field(c, "Bemærkninger", job.Work.Remarks!));
                }
                });
            });
        });
    }

    private static void ComposeCustomerSection(IContainer container, JobReportSummaryResponse job)
    {
        Section(container, "Kundeoplysninger", body =>
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
        Section(container, "Arbejde og kontrolpunkter", body =>
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
        container.Border(1).BorderColor(PdfStyle.BorderColor).Background(PdfStyle.SectionBackground).Padding(PdfStyle.ElementPadding).Column(col =>
        {
            col.Spacing(PdfStyle.CompactGap);
            col.Item().PaddingBottom(PdfStyle.SmallPadding).Text(installation.Name).FontSize(12).Bold().FontColor(PdfStyle.Primary);

            foreach (var category in installation.Categories.OrderBy(x => x.IsIrrelevant).ThenBy(x => x.SortOrder))
            {
                col.Item().Element(c => ComposeCategory(c, category));
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
                return;
            }

            if (category.ControlPoints is not { Count: > 0 })
            {
                col.Item().Text("Ingen kontrolpunkter valgt.").FontSize(10).FontColor(PdfStyle.TextLight).Italic();
                return;
            }

            var controlPoints = category.ControlPoints.OrderBy(x => x.SortOrder).ToList();
            var columnCount = 3;
            var rowCount = (int)Math.Ceiling((double)controlPoints.Count / columnCount);

            col.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });

                for (int i = 0; i < rowCount; i++)
                {
                    table.Cell().Element(c => RenderControlPointCell(c, controlPoints, i * columnCount + 0));
                    table.Cell().Element(c => RenderControlPointCell(c, controlPoints, i * columnCount + 1));
                    table.Cell().Element(c => RenderControlPointCell(c, controlPoints, i * columnCount + 2));
                }
            });
        });
    }

    private static void RenderControlPointCell(IContainer container, IReadOnlyList<InstallationTypeControlPointResponse> controlPoints, int index)
    {
        if (index >= controlPoints.Count)
        {
            container.Padding(2);
            return;
        }

        ControlPointRow(container, controlPoints[index]);
    }

    private static void ControlPointRow(IContainer container, InstallationTypeControlPointResponse controlPoint)
    {
        var marker = controlPoint.IsChecked ? "✓" : "□";
        var color = controlPoint.IsChecked ? Color.FromHex("#166534") : PdfStyle.TextLight;

        container.PaddingVertical(2).Row(row =>
        {
            row.ConstantItem(16).Text(marker).FontSize(PdfStyle.MarkerSize).FontColor(color);
            row.RelativeItem().Column(col =>
            {
                col.Item().Text(controlPoint.Name).FontSize(10).FontColor(PdfStyle.TextDark);
            });
        });
    }

    private static void ComposeObservationsSection(IContainer container, JobReportSummaryResponse job)
    {
        Section(container, "Opgave og observationer", body =>
        {
            body.Column(col =>
            {
                col.Spacing(PdfStyle.GroupGap);
                col.Item().Element(c => LongText(c, "Opgavebeskrivelse", job.Observations.TaskDescription));
                col.Item().Element(c => LongText(c, "Oplysninger til kunden", job.Observations.CustomerObservations));
                col.Item().Element(c => LongText(c, "Tekniske observationer", job.Observations.TechnicalObservations));
            });
        });
    }

    private static void ComposeWorksheetsSection(IContainer container, JobReportSummaryResponse job)
    {
        Section(container, "Arbejdssedler", body =>
        {
            body.Column(col =>
            {
                if (job.Worksheets.Count == 0)
                {
                    col.Item().Text("Ingen arbejdssedler registreret.").FontColor(PdfStyle.TextLight).Italic();
                    return;
                }

                var worksheets = job.Worksheets.OrderBy(x => x.WorkDate).ThenBy(x => x.UserDisplayName).ToList();

                col.Spacing(PdfStyle.CompactGap);
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(4);
                        columns.ConstantColumn(55);
                        columns.ConstantColumn(65);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Element(c => TableHeaderCell(c, "Dato"));
                        header.Cell().Element(c => TableHeaderCell(c, "Medarbejder"));
                        header.Cell().Element(c => TableHeaderCell(c, "Timer"));
                        header.Cell().Element(c => TableHeaderCell(c, "Overnatning"));
                    });

                    foreach (var ws in worksheets)
                    {
                        var bgColor = (worksheets.IndexOf(ws) % 2 == 0) ? Colors.White : PdfStyle.PrimaryLight;

                        table.Cell().Element(c => TableCell(c, FormatDate(ws.WorkDate), bgColor));
                        table.Cell().Element(c => TableCell(c, Value(ws.UserDisplayName), bgColor));
                        table.Cell().Element(c => TableCell(c, FormatDecimal(ws.HoursWorked), bgColor));
                        table.Cell().Element(c => TableCell(c, ws.SleptOnJob ? "Ja" : "Nej", bgColor));
                    }
                });

                col.Item().PaddingTop(PdfStyle.SmallPadding).Row(row =>
                {
                    row.RelativeItem().Element(c => SummaryField(c, "I alt timer", FormatDecimal(job.TotalHours)));
                    row.RelativeItem().Element(c => SummaryField(c, "I alt udlæg", FormatOvernightStays(job.TotalOutlay)));
                });
            });
        });
    }

    private static void ComposeLinksSection(IContainer container, JobReportSummaryResponse job, Uri? jobBaseUri)
    {
        Section(container, "Relaterede sager", body =>
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

    private static void Section(IContainer container, string title, Action<IContainer> compose)
    {
        container.Column(col =>
        {
            col.Item().Background(PdfStyle.PrimaryLight).PaddingVertical(PdfStyle.CardPadding).PaddingHorizontal(PdfStyle.ContentPadding)
                .Text(title.ToUpperInvariant()).FontSize(PdfStyle.SectionTitleSize).Bold().FontColor(PdfStyle.Primary);
            col.Item().Border(1).BorderColor(PdfStyle.BorderColor).BorderTop(0).Padding(PdfStyle.ContentPadding).Element(c => compose(c));
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

    private static void SummaryField(IContainer container, string label, string value)
    {
        container.BorderTop(1).BorderColor(PdfStyle.BorderColor).PaddingTop(PdfStyle.SmallPadding).Column(col =>
        {
            col.Item().Text(label).FontSize(PdfStyle.LabelSize).FontColor(PdfStyle.TextMedium);
            col.Item().Text(value).FontSize(PdfStyle.FieldValueSize).Bold().FontColor(PdfStyle.Primary);
        });
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

    private static void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().PaddingTop(PdfStyle.SectionPadding).LineHorizontal(1).LineColor(PdfStyle.BorderColor);
            col.Item().PaddingTop(PdfStyle.CardPadding).Row(row =>
            {
                row.RelativeItem().Text($"PDF genereret {FormatDateTime(DateTimeOffset.Now)}").FontSize(PdfStyle.SmallTextSize).FontColor(PdfStyle.TextLight);
                row.RelativeItem().AlignCenter().DefaultTextStyle(x => x.FontSize(PdfStyle.SmallTextSize).FontColor(PdfStyle.TextLight)).Text(text =>
                {
                    text.Span("Side ");
                    text.CurrentPageNumber();
                    text.Span(" af ");
                    text.TotalPages();
                });
                row.RelativeItem().AlignRight().Text("WORKSLIP · Jobrapport").FontSize(PdfStyle.SmallTextSize).FontColor(PdfStyle.TextLight);
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
}
