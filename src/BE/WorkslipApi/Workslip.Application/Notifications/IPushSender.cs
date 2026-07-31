using Workslip.Domain.Models;

namespace Workslip.Application.Notifications;

public interface IPushSender
{
    Task<PushSenderResult> SendNotificationAsync(PushSubscriptionRow subscription, string payloadJson, CancellationToken cancellationToken);
}

public sealed record PushSenderResult(
    bool Success,
    string? ErrorMessage,
    bool ShouldDeactivateSubscription);
