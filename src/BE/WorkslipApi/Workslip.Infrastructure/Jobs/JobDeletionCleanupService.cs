using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Workslip.Application.Jobs;

namespace Workslip.Infrastructure.Jobs;

public sealed class JobDeletionCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<JobDeletionCleanupService> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);

            using var timer = new PeriodicTimer(CleanupInterval);
            do
            {
                await PurgeDueJobsAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task PurgeDueJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IJobRepository>();
        var deletedCount = await repository.PurgeDeletionScheduledBeforeAsync(DateTimeOffset.UtcNow, cancellationToken);

        if (deletedCount > 0)
        {
            logger.LogInformation("Scheduled job deletions purged. DeletedCount: {DeletedCount}.", deletedCount);
        }
    }
}
