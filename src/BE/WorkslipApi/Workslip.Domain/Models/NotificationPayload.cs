namespace Workslip.Domain.Models;

public sealed record NotificationPayload(
    Guid JobId,
    string JobNumber,
    string CustomerAddress,
    string NotificationType,
    string RecipientName,
    string Url = "/app",
    string? RejectionNote = null
);
