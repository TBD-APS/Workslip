namespace Workslip.Domain.Models;

public sealed class NotificationDeliveryLogRow
{
    public Guid Id { get; set; }
    public Guid NotificationId { get; set; }
    public Guid SubscriptionId { get; set; }
    public bool Success { get; set; }
    public DateTimeOffset SentUtc { get; set; }
    public string? ErrorMessage { get; set; }
}
