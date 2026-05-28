namespace Workslip.Domain.Models;

public sealed class JobReportRow
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid? CustomerId { get; init; }
    public string? ReportNumber { get; init; }
    public string Status { get; init; } = "";
    public DateTime? ReportDate { get; init; }
    public string? TaskDescription { get; init; }
    public string? CustomerObservations { get; init; }
    public string? TechnicalObservations { get; init; }
    public string InstallationTypesJson { get; init; } = "[]";
    public string? WorkKind { get; init; }
    public string? CustomWorkKind { get; init; }
    public string? Remarks { get; init; }
    public string ClosureFlagsJson { get; init; } = "[]";
    public string? PayloadJson { get; init; }
    public bool IsSoftDeleted { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? SubmittedAt { get; init; }
    public DateTimeOffset? DeletionScheduledAt { get; init; }
}
