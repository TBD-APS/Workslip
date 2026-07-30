using Workslip.Domain.Models;

namespace Workslip.Application.Notifications;

public interface INotificationRepository
{
    Task QueueNotificationAsync(NotificationQueueRow row, CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationQueueRow>> ClaimPendingNotificationsAsync(int batchSize, CancellationToken cancellationToken);
    Task UpdateNotificationStatusAsync(Guid id, string status, int retryCount, DateTimeOffset nextAttemptUtc, string? lastError, CancellationToken cancellationToken);
    Task MarkNotificationCompletedAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<PushSubscriptionRow>> GetActiveSubscriptionsForUserAsync(Guid userId, CancellationToken cancellationToken);
    Task UpdateSubscriptionActiveStatusAsync(Guid subscriptionId, bool isActive, CancellationToken cancellationToken);
    Task LogDeliveryAttemptAsync(NotificationDeliveryLogRow log, CancellationToken cancellationToken);
    Task RegisterSubscriptionAsync(
        Guid userId,
        string endpoint,
        string p256Dh,
        string auth,
        string? userAgent,
        string? replacedEndpoint,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<NotificationQueueRow>> GetHistoryAsync(Guid userId, int limit, int offset, CancellationToken cancellationToken);
    Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken);
    Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken);
}
