using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Workslip.Infrastructure.Notifications;

public sealed class PushNotificationWorker : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PushNotificationWorker> _logger;

    public PushNotificationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<PushNotificationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Temporary error-level trace so the existing Superadmin error dashboard
        // proves that the hosted worker is running in production. Remove after WOR-317.
        _logger.LogError("PUSH TRACE: PushNotificationWorker started and is polling.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processedCount = await ProcessBatchAsync(stoppingToken);
                if (processedCount > 0)
                {
                    _logger.LogError(
                        "PUSH TRACE: Worker claimed and processed a batch containing {ProcessedCount} notifications.",
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
                    exception,
                    "Error occurred during notification processing cycle.");
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
