namespace Workslip.Infrastructure.Models;

public sealed class JobReportRow
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public string ReportNumber { get; init; } = "";
    public string Status { get; init; } = "";
    public string CustomerName { get; init; } = "";
    public string CustomerAddress { get; init; } = "";
    public string? ContactPerson { get; init; }
    public string? Phone { get; init; }
    public DateTime? ReportDate { get; init; }
    public string TaskDescription { get; init; } = "";
    public string? CustomerObservations { get; init; }
    public string InstallationTypesJson { get; init; } = "[]";
    public string WorkKind { get; init; } = "";
    public string? CustomWorkKind { get; init; }
    public string? Remarks { get; init; }
    public string ClosureFlagsJson { get; init; } = "[]";
    public string? PayloadJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? SubmittedAt { get; init; }
}
