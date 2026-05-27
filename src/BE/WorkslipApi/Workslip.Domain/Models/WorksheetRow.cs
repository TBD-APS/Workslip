using System;

namespace Workslip.Domain.Models;

public sealed class WorksheetRow
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid JobId { get; set; }
    public Guid UserId { get; set; }
    public DateOnly WorkDate { get; set; }
    public decimal HoursWorked { get; set; }
    public bool SleptOnJob { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}