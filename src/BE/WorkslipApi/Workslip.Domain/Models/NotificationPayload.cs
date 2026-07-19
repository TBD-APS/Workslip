namespace Workslip.Domain.Models;

public sealed record NotificationPayload(
    Guid JobId,
    string JobNumber,
    string CustomerAddress,
    string NotificationType,
    string Url = "/app"
);
