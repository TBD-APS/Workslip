using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Workslip.Application.Invitations;

namespace Workslip.Infrastructure.Invitations;

public sealed class InviteEntraCleanupService(
    IServiceScopeFactory scopeFactory,
    ILogger<InviteEntraCleanupService> logger) : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(6);

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
                    await CleanupStaleInvitesAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(
                        ex,
                        "Stale invite cleanup failed. Next attempt in {RetryInterval}.",
                        CleanupInterval);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task CleanupStaleInvitesAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var invitationService = scope.ServiceProvider.GetRequiredService<IInvitationService>();
        var cleanedCount = await invitationService.CleanupStaleEntraInvitesAsync(DateTimeOffset.UtcNow, BatchSize, cancellationToken);

        if (cleanedCount > 0)
        {
            logger.LogInformation("Stale invite-owned Entra users cleaned. CleanedCount: {CleanedCount}.", cleanedCount);
        }
    }
}
