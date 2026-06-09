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
    private static readonly Color Primary = Color.FromHex("#1B3A5C");
    private static readonly Color PrimaryLight = Color.FromHex("#E8EDF2");
    private static readonly Color Accent = Color.FromHex("#3B82F6");
    private static readonly Color TextDark = Colors.Black;
    private static readonly Color TextMedium = Color.FromHex("#475569");
    private static readonly Color TextLight = Colors.Grey.Darken1;
    private static readonly Color BorderColor = Color.FromHex("#CBD5E1");
    private static readonly Color SectionBackground = Color.FromHex("#F8FAFC");

    public byte[] Generate(JobReportSummaryResponse job, JobStatus status, Uri? jobBaseUri = null)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Helvetica"));

                page.Header().Element(c => ComposeHeader(c, job, status));
                page.Content().PaddingTop(10).Element(c => ComposeContent(c, job, jobBaseUri));
                page.Footer().Element(c => ComposeFooter(c));
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, JobReportSummaryResponse job, JobStatus status)
    {
        container.Column(col =>
        {
            col.Item().Background(Primary).Padding(12).Row(row =>
            {
                row.RelativeItem().Column(brand =>
                {
                    brand.Item().Text("WORKSLIP").FontSize(16).Bold().FontColor(Colors.White);
                    brand.Item().Text("4V05-rapport").FontSize(8).FontColor(Color.FromHex("#CBD5E1"));
                });

                row.RelativeItem().AlignRight().Column(info =>
                {
                    info.Item().Text(job.ReportNumber ?? ShortId(job.Id)).FontSize(14).Bold().FontColor(Colors.White);
                    info.Item().Text(StatusLabel(status)).FontSize(9).FontColor(StatusColor(status));
                });
            });

            col.Item().Border(1).BorderColor(BorderColor).BorderTop(0).Padding(8).Row(row =>
            {
                row.RelativeItem().Element(c => Field(c, "Kunde", Value(job.Customer.Name)));
                row.RelativeItem().Element(c => Field(c, "Rapportdato", FormatDate(job.Observations.ReportDate)));
                row.RelativeItem().Element(c => Field(c, "Total timer", FormatDecimal(job.TotalHours)));
                row.RelativeItem().Element(c => Field(c, "Overnatninger", FormatOvernightStays(job.TotalOutlay)));
            });
        });
    }

    private static void ComposeContent(IContainer container, JobReportSummaryResponse job, Uri? jobBaseUri)
    {
        container.Column(col =>
        {
            col.Spacing(8);
            col.Item().Element(c => ComposeMetaSection(c, job));
            col.Item().Element(c => ComposeCustomerSection(c, job));
            col.Item().Element(c => ComposeAssignmentSection(c, job));
            col.Item().Element(c => ComposeWorkSection(c, job));
            col.Item().Element(c => ComposeObservationsSection(c, job));
            col.Item().Element(c => ComposeWorksheetsSection(c, job));
            col.Item().Element(c => ComposeLinksSection(c, job, jobBaseUri));
        });
    }

    private static void ComposeMetaSection(IContainer container, JobReportSummaryResponse job)
    {
        Section(container, "Sag", body =>
        {
            body.Column(section =>
            {
                section.Spacing(6);

                if (job.SoftDeleted)
                {
                    section.Item().Background(Color.FromHex("#FEF3C7")).Border(1).BorderColor(Color.FromHex("#F59E0B"))
                        .Padding(6).Text("Rapporten er markeret slettet.").FontSize(8).FontColor(Color.FromHex("#92400E"));
                }

                section.Item().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Element(c => Field(c, "Sagsnummer", job.ReportNumber ?? "-"));
                        col.Item().Element(c => Field(c, "Organisation", Value(job.OrganizationName)));
                        col.Item().Element(c => Field(c, "CVR", Value(job.OrganizationCvr)));
                    });

                    row.ConstantItem(18);

                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Element(c => Field(c, "Status", StatusLabel(job.Status)));
                        col.Item().Element(c => Field(c, "Indsendt", FormatDateTime(job.SubmittedAt)));
                        col.Item().Element(c => Field(c, "Oprettet", FormatDateTime(job.CreatedAt)));
                        col.Item().Element(c => Field(c, "Senest ændret", FormatDateTime(job.UpdatedAt)));
                    });
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
                    col.Item().Element(c => Field(c, "Navn", Value(job.Customer.Name)));
                    col.Item().Element(c => Field(c, "Adresse", Value(job.Customer.Address)));
                    col.Item().Element(c => Field(c, "Email", Value(job.Customer.Email)));
                });

                row.ConstantItem(18);

                row.RelativeItem().Column(col =>
                {
                    col.Item().Element(c => Field(c, "Kontaktperson", Value(job.Customer.ContactPerson)));
                    col.Item().Element(c => Field(c, "Telefon", Value(job.Customer.Phone)));
                });
            });
        });
    }

    private static void ComposeAssignmentSection(IContainer container, JobReportSummaryResponse job)
    {
        Section(container, "Tildelte medarbejdere", body =>
        {
            if (job.AssignedUsers.Count == 0)
            {
                body.Text("Ingen medarbejdere tildelt.").FontColor(TextLight).Italic();
                return;
            }

            body.Column(col =>
            {
                foreach (var user in job.AssignedUsers.OrderBy(user => user.DisplayName))
                {
                    col.Item().Element(c => Bullet(c, user.DisplayName));
                }
            });
        });
    }

    private static void ComposeWorkSection(IContainer container, JobReportSummaryResponse job)
    {
        Section(container, "Arbejde og kontrolpunkter", body =>
        {
            body.Column(col =>
            {
                col.Spacing(8);
                col.Item().Row(row =>
                {
                    row.RelativeItem().Element(c => Field(c, "Arbejdsslag", WorkKindLabel(job.Work.WorkKind)));
                    row.RelativeItem().Element(c => Field(c, "Afslutning", JoinLabels(job.Work.ClosureFlags.Select(flag => flag.Label))));
                });

                if (HasValue(job.Work.Remarks))
                {
                    col.Item().Element(c => Field(c, "Bemærkninger", job.Work.Remarks!));
                }

                if (job.Work.InstallationTypes.Count == 0)
                {
                    col.Item().Text("Ingen anlægstyper valgt.").FontColor(TextLight).Italic();
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
        container.Border(1).BorderColor(BorderColor).Background(SectionBackground).Padding(7).Column(col =>
        {
            col.Spacing(5);
            col.Item().Text(installation.Name).FontSize(10).Bold().FontColor(Primary);

            foreach (var category in installation.Categories.OrderBy(x => x.IsIrrelevant).ThenBy(x => x.SortOrder))
            {
                col.Item().Element(c => ComposeCategory(c, category));
            }
        });
    }

    private static void ComposeCategory(IContainer container, InstallationTypeCategoryResponse category)
    {
        container.Background(Colors.White).Border(1).BorderColor(Color.FromHex("#E2E8F0")).Padding(6).Column(col =>
        {
            col.Spacing(3);
            col.Item().Row(row =>
            {
                row.RelativeItem().Text(category.Name).FontSize(9).SemiBold().FontColor(TextDark);
                if (category.IsIrrelevant)
                {
                    row.AutoItem().Text("Irrelevant").FontSize(8).FontColor(Color.FromHex("#B45309"));
                }
            });

            if (category.IsIrrelevant)
            {
                col.Item().Text("Kategorien er markeret irrelevant.").FontSize(8).FontColor(TextMedium);
                return;
            }

            if (category.ControlPoints.Count == 0)
            {
                col.Item().Text("Ingen kontrolpunkter valgt.").FontSize(8).FontColor(TextLight).Italic();
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
        var marker = controlPoint.IsChecked ? "✓" : "□";
        var color = controlPoint.IsChecked ? Color.FromHex("#166534") : TextLight;

        container.Row(row =>
        {
            row.ConstantItem(14).Text(marker).FontSize(9).FontColor(color);
            row.RelativeItem().Column(col =>
            {
                col.Item().Text(controlPoint.Name).FontSize(8).FontColor(TextDark);
            });
        });
    }

    private static void ComposeObservationsSection(IContainer container, JobReportSummaryResponse job)
    {
        Section(container, "Opgave og observationer", body =>
        {
            body.Column(col =>
            {
                col.Spacing(6);
                col.Item().Element(c => Field(c, "Rapportdato", FormatDate(job.Observations.ReportDate)));
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
                col.Spacing(5);
                col.Item().Row(row =>
                {
                    row.RelativeItem().Element(c => Field(c, "Total timer", FormatDecimal(job.TotalHours)));
                    row.RelativeItem().Element(c => Field(c, "Overnatninger", FormatOvernightStays(job.TotalOutlay)));
                });

                if (job.Worksheets.Count == 0)
                {
                    col.Item().Text("Ingen arbejdssedler registreret.").FontColor(TextLight).Italic();
                    return;
                }

                foreach (var worksheet in job.Worksheets.OrderBy(x => x.WorkDate).ThenBy(x => x.UserDisplayName))
                {
                    col.Item().Element(c => ComposeWorksheet(c, worksheet));
                }
            });
        });
    }

    private static void ComposeWorksheet(IContainer container, WorksheetResponse worksheet)
    {
        var displayName = Value(worksheet.UserDisplayName);
        var overnightStay = worksheet.SleptOnJob ? "Ja" : "Nej";

        container.Border(1).BorderColor(Color.FromHex("#E2E8F0")).Padding(6).Row(row =>
        {
            row.RelativeItem().Element(c => Field(c, "Dato", FormatDate(worksheet.WorkDate)));
            row.RelativeItem().Element(c => Field(c, "Medarbejder", displayName));
            row.ConstantItem(70).Element(c => Field(c, "Timer", FormatDecimal(worksheet.HoursWorked)));
            row.ConstantItem(75).Element(c => Field(c, "Overnatning", overnightStay));
        });
    }

    private static void ComposeLinksSection(IContainer container, JobReportSummaryResponse job, Uri? jobBaseUri)
    {
        Section(container, "Relaterede sager", body =>
        {
            if (job.Links.Count == 0)
            {
                body.Text("Ingen relaterede sager.").FontColor(TextLight).Italic();
                return;
            }

            body.Column(col =>
            {
                col.Spacing(4);
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

        linkContainer.Border(1).BorderColor(Color.FromHex("#E2E8F0")).Padding(6).Text(text =>
        {
            text.Span($"{link.LinkedReportNumber} · {link.LinkedCustomerName}").FontSize(9).FontColor(Accent).Underline();
            text.Span($" ({link.LinkedStatus})").FontSize(8).FontColor(TextMedium);
        });
    }

    private static void Section(IContainer container, string title, Action<IContainer> compose)
    {
        container.Column(col =>
        {
            col.Item().Background(PrimaryLight).PaddingVertical(6).PaddingHorizontal(8)
                .Text(title.ToUpperInvariant()).FontSize(10).Bold().FontColor(Primary);
            col.Item().Border(1).BorderColor(BorderColor).BorderTop(0).Padding(8).Element(c => compose(c));
        });
    }

    private static void Field(IContainer container, string label, string value)
    {
        container.PaddingVertical(2).Column(col =>
        {
            col.Item().Text(label).FontSize(7).FontColor(TextLight);
            col.Item().Text(value).FontSize(9).FontColor(TextDark);
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
            col.Item().Text(label).FontSize(7).FontColor(TextLight);
            col.Item().Text(value!).FontSize(9).FontColor(TextDark);
        });
    }

    private static void Bullet(IContainer container, string title)
    {
        container.Row(row =>
        {
            row.ConstantItem(12).Text("•").FontSize(9).FontColor(Accent);
            row.RelativeItem().Text(title).FontSize(9).FontColor(TextDark);
        });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.Column(col =>
        {
            col.Item().PaddingTop(8).LineHorizontal(1).LineColor(BorderColor);
            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Text($"PDF genereret {DateTimeOffset.Now:dd.MM.yyyy HH:mm}").FontSize(7).FontColor(TextLight);
                row.RelativeItem().AlignRight().Text("WORKSLIP · Jobrapport").FontSize(7).FontColor(TextLight);
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

    private static string FormatDate(DateOnly? value) => value?.ToString("dd.MM.yyyy", DanishCulture) ?? "-";

    private static string FormatDate(DateOnly value) => value.ToString("dd.MM.yyyy", DanishCulture);

    private static string FormatDateTime(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("dd.MM.yyyy HH:mm", DanishCulture) ?? "-";

    private static string FormatDateTime(DateTimeOffset value) =>
        value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", DanishCulture);

    private static string FormatDecimal(decimal? value) =>
        value.HasValue ? value.Value.ToString("0.##", DanishCulture) : "-";

    private static string FormatOvernightStays(int? value) =>
        value.HasValue ? $"{value.Value} {(value.Value == 1 ? "nat" : "nætter")}" : "-";

    private static string StatusLabel(JobStatus status) => status switch
    {
        JobStatus.Draft => "Kladde",
        JobStatus.Submitted => "Indsendt",
        JobStatus.InReview => "Til gennemsyn",
        JobStatus.Approved => "Godkendt",
        JobStatus.Rejected => "Returneret",
        JobStatus.Archived => "Arkiveret",
        _ => status.ToString()
    };

    private static Color StatusColor(JobStatus status) => status switch
    {
        JobStatus.Draft => Color.FromHex("#CBD5E1"),
        JobStatus.Submitted => Color.FromHex("#FACC15"),
        JobStatus.InReview => Color.FromHex("#93C5FD"),
        JobStatus.Approved => Color.FromHex("#86EFAC"),
        JobStatus.Rejected => Color.FromHex("#FCA5A5"),
        JobStatus.Archived => Color.FromHex("#CBD5E1"),
        _ => Colors.White
    };
}
