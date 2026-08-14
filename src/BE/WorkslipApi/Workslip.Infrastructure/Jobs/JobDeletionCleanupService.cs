using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Workslip.Application.Images;
using Workslip.Application.Jobs;
using Workslip.Infrastructure.Schema;

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
                try
                {
                    await PurgeDueJobsAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Scheduled job deletion cleanup failed. Next attempt in {RetryInterval}.",
                        CleanupInterval);
                }
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
        var dbContext = scope.ServiceProvider.GetRequiredService<SqlDbContext>();
        var imageStorage = scope.ServiceProvider.GetRequiredService<IImageStorage>();
        var cutoff = DateTimeOffset.UtcNow;

        var dueJobs = await dbContext.JobReports
            .AsNoTracking()
            .Where(report => report.DeletionScheduledAt != null && report.DeletionScheduledAt <= cutoff)
            .Select(report => new { report.OrganizationId, report.Id })
            .ToArrayAsync(cancellationToken);

        foreach (var job in dueJobs)
        {
            await imageStorage.DeleteJobImagesAsync(
                job.OrganizationId,
                job.Id,
                cancellationToken);
        }

        var deletedCount = await repository.PurgeDeletionScheduledBeforeAsync(cutoff, cancellationToken);

        if (deletedCount > 0)
        {
            logger.LogInformation("Scheduled job deletions purged. DeletedCount: {DeletedCount}.", deletedCount);
        }
    }
}
