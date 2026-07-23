namespace Workslip.Domain.Models;

public sealed class NotificationQueueRow
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public int RetryCount { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? ProcessingStartedUtc { get; set; }
    public DateTimeOffset NextAttemptUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public DateTimeOffset? ReadUtc { get; set; }
    public string? LastError { get; set; }
}
