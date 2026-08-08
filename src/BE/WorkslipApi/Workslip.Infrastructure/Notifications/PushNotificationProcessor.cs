using System.Text.Json;
using Microsoft.Extensions.Logging;
using Workslip.Application.Notifications;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Notifications;

public sealed class PushNotificationProcessor
{
    private const int MaxRetryCount = 5;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly INotificationRepository _notificationRepository;
    private readonly IPushSender _pushSender;
    private readonly INotificationService _notificationService;
    private readonly ILogger<PushNotificationProcessor> _logger;

    public PushNotificationProcessor(
        INotificationRepository notificationRepository,
        IPushSender pushSender,
        INotificationService notificationService,
        ILogger<PushNotificationProcessor> logger)
    {
        _notificationRepository = notificationRepository;
        _pushSender = pushSender;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task<int> ProcessBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        var notifications = await _notificationRepository
            .ClaimPendingNotificationsAsync(batchSize, cancellationToken);

        foreach (var notification in notifications)
        {
            try
            {
                await ProcessNotificationAsync(notification, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await RescheduleUnexpectedFailureAsync(notification, exception, cancellationToken);
            }
        }

        return notifications.Count;
    }

    public async Task ProcessNotificationAsync(
        NotificationQueueRow notification,
        CancellationToken cancellationToken)
    {
        NotificationPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<NotificationPayload>(notification.PayloadJson, JsonOptions);
        }
        catch (JsonException exception)
        {
            await MarkFailedAsync(notification, "Invalid notification payload.", cancellationToken);
            _logger.LogError(
                "Push notification {NotificationId} has an invalid payload. FailureType {FailureType}.",
                notification.Id,
                exception.GetType().Name);
            return;
        }

        if (payload is null)
        {
            await MarkFailedAsync(notification, "Missing notification payload.", cancellationToken);
            _logger.LogError(
                "Push notification {NotificationId} has a missing payload.",
                notification.Id);
            return;
        }

        if (!Enum.TryParse<NotificationType>(
                notification.NotificationType,
                ignoreCase: true,
                out var notificationType))
        {
            await MarkFailedAsync(notification, "Unknown notification type.", cancellationToken);
            _logger.LogError(
                "Push notification {NotificationId} has an unknown notification type.",
                notification.Id);
            return;
        }

        var subscriptions = await _notificationRepository
            .GetActiveSubscriptionsForUserAsync(notification.UserId, cancellationToken);
        if (subscriptions.Count == 0)
        {
            await _notificationRepository.MarkNotificationCompletedAsync(
                notification.Id,
                cancellationToken);

            _logger.LogDebug(
                "Push notification {NotificationId} has no active subscriptions. NotificationType {NotificationType}. RetryCount {RetryCount}.",
                notification.Id,
                notification.NotificationType,
                notification.RetryCount);
            return;
        }

        var successfulSubscriptionIds = await _notificationRepository
            .GetSuccessfulSubscriptionIdsAsync(notification.Id, cancellationToken);
        var pendingSubscriptions = subscriptions
            .Where(subscription => !successfulSubscriptionIds.Contains(subscription.Id))
            .ToArray();

        _logger.LogDebug(
            "Processing push notification {NotificationId} of type {NotificationType} with {ActiveSubscriptionCount} active subscriptions and {PendingSubscriptionCount} pending deliveries.",
            notification.Id,
            notification.NotificationType,
            subscriptions.Count,
            pendingSubscriptions.Length);

        if (pendingSubscriptions.Length == 0)
        {
            await _notificationRepository.MarkNotificationCompletedAsync(
                notification.Id,
                cancellationToken);
            return;
        }

        var (title, body) = _notificationService.GetLocalizedText(
            notificationType,
            payload.JobNumber,
            payload.CustomerAddress,
            payload.RecipientName,
            payload.RejectionNote);
        var pushPayload = JsonSerializer.Serialize(new
        {
            title,
            options = new
            {
                body,
                icon = "/icons/icon-192.png",
                badge = "/icons/badge.png",
                tag = $"job-{payload.JobId}",
                data = new { url = payload.Url }
            }
        }, JsonOptions);

        var hasTemporaryFailure = false;
        var successfulDeliveries = 0;
        var expiredDeliveries = 0;
        var temporaryFailures = 0;

        foreach (var subscription in pendingSubscriptions)
        {
            var result = await _pushSender.SendNotificationAsync(
                subscription,
                pushPayload,
                cancellationToken);

            await _notificationRepository.LogDeliveryAttemptAsync(
                new NotificationDeliveryLogRow
                {
                    Id = Guid.NewGuid(),
                    NotificationId = notification.Id,
                    SubscriptionId = subscription.Id,
                    Success = result.Success,
                    SentUtc = DateTimeOffset.UtcNow,
                    ErrorMessage = GetPersistedDeliveryError(result)
                },
                cancellationToken);

            if (result.Success)
            {
                successfulDeliveries++;
                continue;
            }

            if (result.IsExpired)
            {
                expiredDeliveries++;
                await _notificationRepository.UpdateSubscriptionActiveStatusAsync(
                    subscription.Id,
                    isActive: false,
                    cancellationToken);
                continue;
            }

            temporaryFailures++;
            hasTemporaryFailure = true;
        }

        if (!hasTemporaryFailure)
        {
            await _notificationRepository.MarkNotificationCompletedAsync(
                notification.Id,
                cancellationToken);

            if (expiredDeliveries > 0)
            {
                _logger.LogWarning(
                    "Push delivery completed with expired subscriptions for notification {NotificationId} of type {NotificationType}. Successful {SuccessfulCount}, expired {ExpiredCount}.",
                    notification.Id,
                    notification.NotificationType,
                    successfulDeliveries,
                    expiredDeliveries);
            }
            else
            {
                _logger.LogDebug(
                    "Push delivery completed for notification {NotificationId} of type {NotificationType}. Successful {SuccessfulCount}.",
                    notification.Id,
                    notification.NotificationType,
                    successfulDeliveries);
            }

            return;
        }

        await RescheduleDeliveryFailureAsync(
            notification,
            "Temporary push delivery failure.",
            successfulDeliveries,
            expiredDeliveries,
            temporaryFailures,
            cancellationToken);
    }

    private async Task RescheduleUnexpectedFailureAsync(
        NotificationQueueRow notification,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var nextRetryCount = notification.RetryCount + 1;
        var status = nextRetryCount >= MaxRetryCount ? "Failed" : "Pending";
        var nextAttemptUtc = status == "Failed"
            ? DateTimeOffset.UtcNow
            : DateTimeOffset.UtcNow.Add(GetRetryDelay(nextRetryCount));

        await _notificationRepository.UpdateNotificationStatusAsync(
            notification.Id,
            status,
            nextRetryCount,
            nextAttemptUtc,
            $"Unexpected push processing failure ({exception.GetType().Name}).",
            cancellationToken);

        if (status == "Failed")
        {
            _logger.LogError(
                "Push notification {NotificationId} failed after an unexpected processing failure. RetryCount {RetryCount}. FailureType {FailureType}.",
                notification.Id,
                nextRetryCount,
                exception.GetType().Name);
        }
        else
        {
            _logger.LogWarning(
                "Push notification {NotificationId} was rescheduled after an unexpected processing failure. RetryCount {RetryCount}. FailureType {FailureType}.",
                notification.Id,
                nextRetryCount,
                exception.GetType().Name);
        }
    }

    private async Task RescheduleDeliveryFailureAsync(
        NotificationQueueRow notification,
        string lastError,
        int successfulDeliveries,
        int expiredDeliveries,
        int temporaryFailures,
        CancellationToken cancellationToken)
    {
        var nextRetryCount = notification.RetryCount + 1;
        if (nextRetryCount >= MaxRetryCount)
        {
            await _notificationRepository.UpdateNotificationStatusAsync(
                notification.Id,
                "Failed",
                nextRetryCount,
                DateTimeOffset.UtcNow,
                "Push delivery failed after the maximum retry count.",
                cancellationToken);

            _logger.LogError(
                "Push delivery permanently failed for notification {NotificationId}. NotificationType {NotificationType}. SuccessfulCount {SuccessfulCount}. ExpiredCount {ExpiredCount}. TemporaryFailureCount {TemporaryFailureCount}. RetryCount {RetryCount}.",
                notification.Id,
                notification.NotificationType,
                successfulDeliveries,
                expiredDeliveries,
                temporaryFailures,
                nextRetryCount);
            return;
        }

        await _notificationRepository.UpdateNotificationStatusAsync(
            notification.Id,
            "Pending",
            nextRetryCount,
            DateTimeOffset.UtcNow.Add(GetRetryDelay(nextRetryCount)),
            lastError,
            cancellationToken);

        _logger.LogWarning(
            "Push delivery will be retried for notification {NotificationId}. NotificationType {NotificationType}. SuccessfulCount {SuccessfulCount}. ExpiredCount {ExpiredCount}. TemporaryFailureCount {TemporaryFailureCount}. RetryCount {RetryCount}.",
            notification.Id,
            notification.NotificationType,
            successfulDeliveries,
            expiredDeliveries,
            temporaryFailures,
            nextRetryCount);
    }

    private Task MarkFailedAsync(
        NotificationQueueRow notification,
        string error,
        CancellationToken cancellationToken) =>
        _notificationRepository.UpdateNotificationStatusAsync(
            notification.Id,
            "Failed",
            notification.RetryCount,
            DateTimeOffset.UtcNow,
            error,
            cancellationToken);

    private static string? GetPersistedDeliveryError(PushSenderResult result)
    {
        if (result.Success)
        {
            return null;
        }

        return result.IsExpired
            ? "Push subscription expired."
            : "Push provider returned a temporary delivery failure.";
    }

    private static TimeSpan GetRetryDelay(int retryCount) => retryCount switch
    {
        1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(5),
        3 => TimeSpan.FromMinutes(15),
        _ => TimeSpan.FromHours(1)
    };
}
