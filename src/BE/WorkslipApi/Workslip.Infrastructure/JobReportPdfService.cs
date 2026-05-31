using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Workslip.Application.Jobs;
using Workslip.Domain;

namespace Workslip.Api.Services;

public interface IJobReportPdfService
{
    byte[] Generate(JobReportSummaryResponse job, JobStatus status);
}

public sealed class JobReportPdfService : IJobReportPdfService
{
    private static readonly Color Primary = Color.FromHex("#1B3A5C");
    private static readonly Color PrimaryLight = Color.FromHex("#E8EDF2");
    private static readonly Color Accent = Color.FromHex("#3B82F6");
    private static readonly Color TextDark = Colors.Black;
    private static readonly Color TextMedium = Color.FromHex("#475569");
    private static readonly Color TextLight = Colors.Grey.Darken1;
    private static readonly Color BorderColor = Color.FromHex("#CBD5E1");

    public byte[] Generate(JobReportSummaryResponse job, JobStatus status)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Helvetica"));

                page.Header().Element(c => ComposeHeader(c, job, status));
                page.Content().Element(c => ComposeContent(c, job));
                page.Footer().Element(c => ComposeFooter(c, job));
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, JobReportSummaryResponse job, JobStatus status)
    {
        container.Column(col =>
        {
            col.Item().Background(Primary).Padding(12).Row(row =>
            {
                row.ConstantItem(120).AlignMiddle().Column(brand =>
                {
                    brand.Item().Text("WORKSLIP").FontSize(16).Bold().FontColor(Colors.White);
                    brand.Item().Text("Digital arbejdsseddel").FontSize(8).FontColor(Color.FromHex("#94A3B8"));
                });

                row.RelativeItem().AlignMiddle().AlignCenter().Column(title =>
                {
                    title.Item().Text("ARBEJDSSEDDEL 4V05").FontSize(18).Bold().FontColor(Colors.White);
                });

                row.ConstantItem(120).AlignMiddle().AlignRight().Column(info =>
                {
                    info.Item().Text($"Rapport nr.").FontSize(7).FontColor(Color.FromHex("#94A3B8"));
                    info.Item().Text(job.ReportNumber).FontSize(11).Bold().FontColor(Colors.White);
                });
            });

            col.Item().PaddingTop(8).Row(row =>
            {
                row.RelativeItem(3).Border(1).BorderColor(BorderColor).Padding(8).Column(left =>
                {
                    left.Item().Text("STATUS").FontSize(7).FontColor(TextLight);
                    left.Item().PaddingTop(2).Text(StatusLabel(status)).FontSize(11).Bold().FontColor(StatusColor(status));
                    left.Item().PaddingTop(4).Text(job.SubmittedAt is not null
                        ? $"Indsendt: {job.SubmittedAt:dd.MM.yyyy HH:mm}"
                        : "Ikke indsendt").FontSize(8).FontColor(TextLight);
                });

                row.ConstantItem(10);

                row.RelativeItem(4).Border(1).BorderColor(BorderColor).Padding(8).Column(mid =>
                {
                    mid.Item().Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("ANLÆGSTYPE").FontSize(7).FontColor(TextLight);
                            c.Item().PaddingTop(2).Text(string.Join(", ", job.ControlInstallationTypes)).FontSize(9).FontColor(TextDark);
                        });
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("ARBEJDSTYPE").FontSize(7).FontColor(TextLight);
                            var workKindLabel = string.IsNullOrEmpty(job.Work.CustomWorkKind)
                                ? job.Work.WorkKind
                                : $"{job.Work.WorkKind} - {job.Work.CustomWorkKind}";
                            c.Item().PaddingTop(2).Text(workKindLabel).FontSize(9).FontColor(TextDark);
                        });
                    });
                });

                row.ConstantItem(10);

                row.RelativeItem(2).Border(1).BorderColor(BorderColor).Padding(8).Column(right =>
                {
                    right.Item().Text("DATO").FontSize(7).FontColor(TextLight);
                    right.Item().PaddingTop(2).Text(job.Observations.ReportDate?.ToString("dd.MM.yyyy") ?? "-").FontSize(11).Bold().FontColor(TextDark);
                });
            });
        });
    }

    private static void ComposeContent(IContainer container, JobReportSummaryResponse job)
    {
        container.Column(col =>
        {
            col.Item().PaddingTop(10).Element(c => ComposeCustomerSection(c, job));

            col.Item().PaddingTop(8).Element(c => ComposeDescriptionSection(c, job));

            if (job.ControlInstallationTypes.Count != 0)
            {
                col.Item().PaddingTop(8).Element(c => ComposeControlPointsSection(c, job));
            }

            col.Item().PaddingTop(8).Element(c => ComposeRemarksSection(c, job));
        });
    }

    private static void ComposeCustomerSection(IContainer container, JobReportSummaryResponse job)
    {
        container.Column(col =>
        {
            col.Item().Background(PrimaryLight).PaddingVertical(6).PaddingHorizontal(8).Row(r =>
            {
                r.AutoItem().Text("KUNDEOPLYSNINGER").FontSize(10).Bold().FontColor(Primary);
            });

            col.Item().Border(1).BorderColor(BorderColor).BorderTop(0).Padding(8).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Element(e => Field(e, "Kunde", job.Customer?.Name ?? "-"));
                    c.Item().Element(e => Field(e, "Email", job.Customer?.Email ?? "-"));
                });
                row.ConstantItem(20);
                row.RelativeItem().Column(c =>
                {
                    c.Item().Element(e => Field(e, "Adresse", job.Customer?.Address ?? "-"));
                    c.Item().Element(e => Field(e, "Kontaktperson", job.Customer?.ContactPerson ?? "-"));
                    c.Item().Element(e => Field(e, "Telefon", job.Customer?.Phone ?? "-"));
                });
            });
        });
    }

    private static void Field(IContainer container, string label, string value)
    {
        container.PaddingVertical(2).Row(row =>
        {
            row.ConstantItem(80).Text(label).FontSize(8).FontColor(TextLight);
            row.RelativeItem().Text(value).FontSize(9).FontColor(TextDark);
        });
    }

    private static void ComposeDescriptionSection(IContainer container, JobReportSummaryResponse job)
    {
        container.Column(col =>
        {
            col.Item().Background(PrimaryLight).PaddingVertical(6).PaddingHorizontal(8).Row(r =>
            {
                r.AutoItem().Text("OPGAVEBESKRIVELSE").FontSize(10).Bold().FontColor(Primary);
                if (!string.IsNullOrEmpty(job.Observations.CustomerObservations))
                {
                    r.RelativeItem().PaddingLeft(20).Text("+ KUNDEOBSERVATIONER").FontSize(8).FontColor(Accent).AlignRight();
                }
            });

            col.Item().Border(1).BorderColor(BorderColor).BorderTop(0).Padding(8).Column(c =>
            {
                c.Item().Text(job.Observations.TaskDescription ?? "-").FontSize(9).FontColor(TextDark);
                if (!string.IsNullOrEmpty(job.Observations.CustomerObservations))
                {
                    c.Item().PaddingTop(6).LineHorizontal(0.5f).LineColor(BorderColor);
                    c.Item().PaddingTop(4).Text("Kundeobservationer:").FontSize(8).FontColor(TextLight).SemiBold();
                    c.Item().Text(job.Observations.CustomerObservations).FontSize(9).FontColor(TextDark);
                }
            });
        });
    }

    private static void ComposeControlPointsSection(IContainer container, JobReportSummaryResponse job)
    {
        container.Column(col =>
        {
            col.Item().Background(PrimaryLight).PaddingVertical(6).PaddingHorizontal(8).Row(r =>
            {
                r.AutoItem().Text("KONTROLPUNKTER").FontSize(10).Bold().FontColor(Primary);
            });

            foreach (var inst in job.ControlInstallationTypes)
            {
                col.Item().Border(1).BorderColor(BorderColor).BorderTop(0).Background(Color.FromHex("#F8FAFC")).Padding(6).Column(instCol =>
                {
                    instCol.Item().Background(Primary).PaddingVertical(4).PaddingHorizontal(6).Row(r =>
                    {
                        r.AutoItem().Text(inst.InstallationTypeId.ToUpper()).FontSize(9).Bold().FontColor(Colors.White);
                    });

                    foreach (var sub in inst.Subcategories)
                    {
                        instCol.Item().PaddingTop(6).Column(subCol =>
                        {
                            subCol.Item().Text(sub.SubcategoryId).FontSize(8).FontColor(Primary).SemiBold();

                            subCol.Item().PaddingTop(3).Row(header =>
                            {
                                header.ConstantItem(20);
                                header.RelativeItem(3).Text("Kontrolpunkt").FontSize(7).FontColor(TextLight);
                                header.RelativeItem(7).Text("Note").FontSize(7).FontColor(TextLight);
                            });

                            foreach (var check in sub.ControlChecks)
                            {
                                subCol.Item().PaddingVertical(1).Row(row =>
                                {
                                    row.ConstantItem(20).PaddingTop(1).Element(c =>
                                    {
                                        if (check.Checked)
                                        {
                                            c.Background(Color.FromHex("#16A34A")).Padding(2).AlignCenter()
                                                .Text("✓").FontSize(8).Bold().FontColor(Colors.White);
                                        }
                                        else
                                        {
                                            c.Border(1).BorderColor(BorderColor).Padding(2).AlignCenter()
                                                .Text("").FontSize(8);
                                        }
                                    });

                                    row.RelativeItem(3).PaddingLeft(4).AlignMiddle()
                                        .Text(check.ItemId).FontSize(8).FontColor(TextDark);

                                    row.RelativeItem(7).PaddingLeft(4).AlignMiddle()
                                        .Text(string.IsNullOrEmpty(check.Note) ? "-" : check.Note)
                                        .FontSize(8).FontColor(string.IsNullOrEmpty(check.Note) ? TextLight : TextDark)
                                        .Italic(string.IsNullOrEmpty(check.Note));
                                });

                                subCol.Item().PaddingBottom(2).LineHorizontal(0.3f).LineColor(Color.FromHex("#F1F5F9"));
                            }
                        });
                    }
                });
            }

            if (!string.IsNullOrEmpty(job.Observations.TechnicalObservations))
            {
                col.Item().Border(1).BorderColor(BorderColor).BorderTop(0).Padding(8).Column(c =>
                {
                    c.Item().Text("TEKNISKE OBSERVATIONER").FontSize(8).FontColor(TextLight).SemiBold();
                    c.Item().PaddingTop(2).Text(job.Observations.TechnicalObservations).FontSize(9).FontColor(TextDark);
                });
            }
        });
    }

    [Obsolete]
    private static void ComposeRemarksSection(IContainer container, JobReportSummaryResponse job)
    {
        var hasRemarks = !string.IsNullOrEmpty(job.Work.Remarks);
        var hasClosureFlags = job.Work.ClosureFlags.Count != 0;

        if (!hasRemarks && !hasClosureFlags)
            return;

        container.Column(col =>
        {
            col.Item().Background(PrimaryLight).PaddingVertical(6).PaddingHorizontal(8).Row(r =>
            {
                r.AutoItem().Text("BEMÆRKNINGER").FontSize(10).Bold().FontColor(Primary);
                if (hasClosureFlags)
                {
                    r.RelativeItem().PaddingLeft(20)
                        .Text("+ AFSLUTNING").FontSize(8).FontColor(Accent).AlignRight();
                }
            });

            col.Item().Border(1).BorderColor(BorderColor).BorderTop(0).Padding(8).Row(row =>
            {
                if (hasRemarks)
                {
                    row.RelativeItem(2).Column(c =>
                    {
                        c.Item().Text("Bemærkninger:").FontSize(8).FontColor(TextLight).SemiBold();
                        c.Item().PaddingTop(2).Text(job.Work.Remarks).FontSize(9).FontColor(TextDark);
                    });
                }

                if (hasClosureFlags)
                {
                    if (hasRemarks)
                        row.ConstantItem(16);

                    row.RelativeItem(1).Column(c =>
                    {
                        c.Item().Text("Afslutningsmarkører:").FontSize(8).FontColor(TextLight).SemiBold();
                        c.Item().PaddingTop(2).Column(flags =>
                        {
                            foreach (var flag in job.Work.ClosureFlags)
                            {
                                flags.Item().Row(fr =>
                                {
                                    fr.ConstantItem(14).Text("›").FontSize(10).FontColor(Accent);
                                    fr.RelativeItem().Text(flag).FontSize(9).FontColor(TextDark);
                                });
                            }
                        });
                    });
                }
            });
        });
    }

    private static void ComposeFooter(IContainer container, JobReportSummaryResponse job)
    {
        container.Column(col =>
        {
            col.Item().PaddingTop(8).LineHorizontal(1).LineColor(BorderColor);

            col.Item().PaddingTop(6).Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Dokument oprettet").FontSize(7).FontColor(TextLight);
                    c.Item().Text($"{job.CreatedAt:dd.MM.yyyy HH:mm}").FontSize(8).FontColor(TextMedium);
                });

                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("Sidst opdateret").FontSize(7).FontColor(TextLight);
                    c.Item().Text($"{job.UpdatedAt:dd.MM.yyyy HH:mm}").FontSize(8).FontColor(TextMedium);
                });

                row.RelativeItem().Column(c =>
                {
                    c.Item().AlignRight().Text("WORKSLIP").FontSize(7).FontColor(TextLight);
                    c.Item().AlignRight().Text("Digital arbejdsseddel · workslip.app").FontSize(7).FontColor(TextLight);
                });
            });
        });
    }

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
        JobStatus.Draft => TextMedium,
        JobStatus.Submitted => Color.FromHex("#CA8A04"),
        JobStatus.InReview => Accent,
        JobStatus.Approved => Color.FromHex("#16A34A"),
        JobStatus.Rejected => Color.FromHex("#DC2626"),
        JobStatus.Archived => TextMedium,
        _ => TextDark
    };
}
