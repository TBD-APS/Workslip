namespace Workslip.Domain.Models;

public sealed class JobAssignmentRow
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid ReportId { get; init; }
    public Guid UserId { get; init; }
    public Guid? AssignedByUserId { get; init; }
    public DateTimeOffset AssignedAt { get; init; }
}
