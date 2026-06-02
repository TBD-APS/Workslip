namespace Workslip.Domain.Models;

public sealed class JobReportRow
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid? CustomerId { get; init; }
    public CustomerRow? CustomerRow { get; set; }
    public string? ReportNumber { get; init; }
    public string Status { get; init; } = "";
    public DateTime? ReportDate { get; init; }
    public string? TaskDescription { get; init; }
    public string? CustomerObservations { get; init; }
    public string? TechnicalObservations { get; init; }
    public List<JobReportInstallationRow>? Installations { get; set; } = new();
    public JobWorkKindRow? WorkKindRow { get; set; }
    public Guid? WorkKindId { get; set; }
    public string? CustomWorkKind { get; init; }
    public string? Remarks { get; init; }
    public List<JobReportClosureFlagRow> ClosureFlags { get; set; } = new();
    public List<JobReportLinkRow> Links{ get; set; } = new();
    public bool IsSoftDeleted { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? SubmittedAt { get; init; }
    public DateTimeOffset? DeletionScheduledAt { get; init; }
}
