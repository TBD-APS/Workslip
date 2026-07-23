namespace Workslip.Domain.Models;

public sealed class IdempotencyRecordRow
{
    public Guid Id { get; init; }
    public string Scope { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public string ReservationToken { get; set; } = string.Empty;
    public bool Completed { get; set; }
    public int StatusCode { get; set; }
    public string? ResponseJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}
