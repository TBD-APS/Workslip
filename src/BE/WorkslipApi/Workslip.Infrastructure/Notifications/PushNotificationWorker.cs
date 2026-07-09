using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Workslip.Application.Notifications;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Notifications;

public sealed class PushNotificationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PushNotificationWorker> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    public PushNotificationWorker(IServiceScopeFactory scopeFactory, ILogger<PushNotificationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PushNotificationWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processedCount = await ProcessBatchAsync(stoppingToken);

                // If we processed a full batch (50), there might be more immediately waiting, so don't delay
                if (processedCount >= 50)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during notification processing cycle.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("PushNotificationWorker stopped.");
    }

    private async Task<int> ProcessBatchAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var pushSender = scope.ServiceProvider.GetRequiredService<IPushSender>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var notifications = await repo.ClaimPendingNotificationsAsync(50, stoppingToken);
        if (notifications.Count == 0)
        {
            return 0;
        }

        foreach (var notification in notifications)
        {
            await ProcessNotificationAsync(notification, repo, pushSender, notificationService, stoppingToken);
        }

        return notifications.Count;
    }

    private async Task ProcessNotificationAsync(
        NotificationQueueRow notification,
        INotificationRepository repo,
        IPushSender pushSender,
        INotificationService notificationService,
        CancellationToken stoppingToken)
    {
        NotificationPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<NotificationPayload>(notification.PayloadJson, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize notification payload. Payload: {PayloadJson}", notification.PayloadJson);
            await repo.UpdateNotificationStatusAsync(notification.Id, "Failed", notification.RetryCount, DateTimeOffset.UtcNow, $"Invalid payload JSON: {ex.Message}", stoppingToken);
            return;
        }

        if (payload == null)
        {
            await repo.UpdateNotificationStatusAsync(notification.Id, "Failed", notification.RetryCount, DateTimeOffset.UtcNow, "Payload is null", stoppingToken);
            return;
        }

        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["NotificationId"] = notification.Id,
            ["UserId"] = notification.UserId,
            ["NotificationType"] = notification.NotificationType,
            ["JobId"] = payload.JobId
        });

        var subscriptions = await repo.GetActiveSubscriptionsForUserAsync(notification.UserId, stoppingToken);
        if (subscriptions.Count == 0)
        {
            _logger.LogInformation("No active push subscriptions found for user.");
            await repo.MarkNotificationCompletedAsync(notification.Id, stoppingToken);
            return;
        }

        if (!Enum.TryParse<NotificationType>(notification.NotificationType, out var type))
        {
            _logger.LogError("Unknown notification type: {NotificationType}", notification.NotificationType);
            await repo.UpdateNotificationStatusAsync(notification.Id, "Failed", notification.RetryCount, DateTimeOffset.UtcNow, $"Unknown notification type: {notification.NotificationType}", stoppingToken);
            return;
        }

        var (title, body) = notificationService.GetLocalizedText(type, payload.JobNumber, payload.CustomerAddress);

        var devicePayload = new
        {
            title,
            options = new
            {
                body,
                icon = "/icons/icon-192.png",
                badge = "/icons/badge.png",
                tag = $"job-{payload.JobId}",
                data = new
                {
                    url = $"/jobs/{payload.JobId}"
                }
            }
        };

        var devicePayloadJson = JsonSerializer.Serialize(devicePayload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var hasTemporaryFailure = false;
        string? lastErrorMessage = null;

        foreach (var sub in subscriptions)
        {
            var result = await pushSender.SendNotificationAsync(sub, devicePayloadJson, stoppingToken);

            var log = new NotificationDeliveryLogRow
            {
                Id = Guid.NewGuid(),
                NotificationId = notification.Id,
                SubscriptionId = sub.Id,
                Success = result.Success,
                SentUtc = DateTimeOffset.UtcNow,
                ErrorMessage = result.ErrorMessage
            };

            await repo.LogDeliveryAttemptAsync(log, stoppingToken);

            if (result.Success)
            {
                _logger.LogInformation("Push notification sent. NotificationId={NotificationId}", notification.Id);
            }
            else
            {
                lastErrorMessage = result.ErrorMessage;
                if (result.IsExpired)
                {
                    _logger.LogInformation("Subscription is expired/invalid. Disabling subscription. SubscriptionId={SubscriptionId}", sub.Id);
                    await repo.UpdateSubscriptionActiveStatusAsync(sub.Id, false, stoppingToken);
                }
                else
                {
                    hasTemporaryFailure = true;
                    _logger.LogWarning("Push notification failed. RetryCount={RetryCount}", notification.RetryCount);
                }
            }
        }

        if (hasTemporaryFailure)
        {
            var nextRetryCount = notification.RetryCount + 1;
            if (nextRetryCount >= 5)
            {
                _logger.LogError("Notification permanently failed. LastError: {LastError}", lastErrorMessage);
                await repo.UpdateNotificationStatusAsync(notification.Id, "Failed", nextRetryCount, DateTimeOffset.UtcNow, lastErrorMessage ?? "Max retries exceeded", stoppingToken);
            }
            else
            {
                var nextAttempt = CalculateNextAttempt(nextRetryCount);
                _logger.LogWarning("Push notification failed. Scheduling retry. RetryCount={RetryCount}", nextRetryCount);
                await repo.UpdateNotificationStatusAsync(notification.Id, "Pending", nextRetryCount, nextAttempt, lastErrorMessage, stoppingToken);
            }
        }
        else
        {
            await repo.MarkNotificationCompletedAsync(notification.Id, stoppingToken);
        }
    }

    private static DateTimeOffset CalculateNextAttempt(int retryCount)
    {
        var now = DateTimeOffset.UtcNow;
        return retryCount switch
        {
            0 => now,
            1 => now.AddMinutes(1),
            2 => now.AddMinutes(5),
            3 => now.AddMinutes(15),
            _ => now.AddHours(1)
        };
    }
}
