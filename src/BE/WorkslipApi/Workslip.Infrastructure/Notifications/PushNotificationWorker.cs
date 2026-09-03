using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Workslip.Application.Notifications;

namespace Workslip.Infrastructure.Notifications;

public sealed class PushNotificationWorker : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IVapidPublicKeyProvider _vapidProvider;
    private readonly ILogger<PushNotificationWorker> _logger;

    public PushNotificationWorker(
        IServiceScopeFactory scopeFactory,
        IVapidPublicKeyProvider vapidProvider,
        ILogger<PushNotificationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _vapidProvider = vapidProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Push notification worker started.");

        if (!_vapidProvider.IsConfigured)
        {
            _logger.LogWarning("Push notification worker started but VAPID is not configured. Push notifications are disabled until the key is provisioned.");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.LogInformation("PushNotificationWorker stopped.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processedCount = await ProcessBatchAsync(stoppingToken);
                if (processedCount > 0)
                {
                    _logger.LogDebug(
                        "Push notification worker claimed a batch. ClaimedCount {ClaimedCount}.",
                        processedCount);
                }

                if (processedCount >= BatchSize)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    "Push notification processing cycle failed. FailureType {FailureType}.",
                    exception.GetType().Name);
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("PushNotificationWorker stopped.");
    }

    private async Task<int> ProcessBatchAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<PushNotificationProcessor>();
        return await processor.ProcessBatchAsync(BatchSize, stoppingToken);
    }
}
