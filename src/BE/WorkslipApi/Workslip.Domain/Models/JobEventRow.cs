namespace Workslip.Domain.Models;

public sealed class JobEventRow
{
    public Guid Id { get; init; }
    public Guid ReportId { get; init; }
    public Guid? ActorId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string? BeforeJson { get; init; }
    public string? AfterJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
