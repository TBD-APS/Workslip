using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;

namespace Workslip.Infrastructure.Repositories;

public sealed class RefreshSessionCleanupService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    RefreshSessionPolicy policy,
    ILogger<RefreshSessionCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IRefreshSessionRepository>();
                var deleted = await repository.DeleteExpiredFamiliesAsync(
                    timeProvider.GetUtcNow().Subtract(policy.CleanupRetention), stoppingToken);
                if (deleted > 0)
                {
                    logger.LogInformation("Expired refresh-session rows removed. DeletedCount: {DeletedCount}.", deleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Refresh-session cleanup failed; it will be retried.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
