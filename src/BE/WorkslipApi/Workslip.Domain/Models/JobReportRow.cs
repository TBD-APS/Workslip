namespace Workslip.Domain.Models;

public sealed class JobReportRow : IJobRelated
{
    public Guid Id { get; init; }
    public Guid JobReportId => Id;
    public Guid OrganizationId { get; init; }
    // Nullable to match the WOR-385 expand-phase column, which is NULL for job rows created
    // before the Filial backfill/triggers. A non-nullable Guid here makes EF materialize such
    // rows into a non-nullable property and throw on read (the WOR-405 "500 opening an existing
    // job"). WOR-398 contracts the column to NOT NULL, at which point this can return to Guid.
    public Guid? FilialId { get; set; }
    public OrganizationRow? OrganizationRow { get; set; }
    public Guid? CustomerId { get; init; }
    public CustomerRow? CustomerRow { get; set; }
    public string? CustomerName { get; init; }
    public string? CustomerEmail { get; init; }
    public string? CustomerPhone { get; init; }
    public string? CustomerAddress { get; init; }
    public string? CustomerContactPerson { get; set; }
    public string? DestinationAddress { get; init; }
    public string? DestinationZipCode { get; init; }
    public string? DestinationCity { get; init; }
    public string? ReportNumber { get; init; }
    public string Status { get; init; } = "";
    public JobType JobType { get; init; } = JobType.KLS;
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
    public bool IsInAuditorScope { get; set; } = true;
    public string? AuditorScopeReason { get; set; }
    public bool IsSoftDeleted { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? DeletionScheduledAt { get; init; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public Guid? SubmittedByUserId { get; set; }
    public string? RejectionNote { get; set; }
}
