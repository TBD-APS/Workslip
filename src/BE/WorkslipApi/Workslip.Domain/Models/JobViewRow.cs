namespace Workslip.Domain.Models;

public sealed class JobViewRow
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public Guid UserId { get; set; }
    public string ViewType { get; set; } = string.Empty;
    public DateTimeOffset ViewedAt { get; set; }
}
