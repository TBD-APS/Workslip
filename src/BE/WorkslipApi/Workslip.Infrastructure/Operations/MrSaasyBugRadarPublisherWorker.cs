using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Workslip.Application.Diagnostics;

namespace Workslip.Infrastructure.Operations;

public sealed class MrSaasyBugRadarPublisherWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MrSaasyBugRadarOptions> options,
    ILogger<MrSaasyBugRadarPublisherWorker> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);
    private readonly MrSaasyBugRadarOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("MR SAAS'y Bug Radar publishing is disabled.");
            return;
        }

        try
        {
            using var validationScope = scopeFactory.CreateScope();
            validationScope.ServiceProvider
                .GetRequiredService<MrSaasyBugRadarCheckpointPublisher>()
                .ValidateOptions();
        }
        catch (Exception exception) when (exception is InvalidOperationException or UriFormatException)
        {
            logger.LogCritical(exception, "MR SAAS'y Bug Radar publishing is disabled because its configuration is invalid.");
            return;
        }

        var interval = TimeSpan.FromMinutes(_options.RefreshIntervalMinutes);
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
            using var timer = new PeriodicTimer(interval);

            do
            {
                try
                {
                    await PublishAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "MR SAAS'y Bug Radar publishing failed. Next attempt in {RetryInterval}.",
                        interval);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task PublishAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var diagnostics = scope.ServiceProvider.GetRequiredService<IErrorDiagnosticsService>();
        var publisher = scope.ServiceProvider.GetRequiredService<MrSaasyBugRadarCheckpointPublisher>();
        var result = await diagnostics.GetAsync(
            new ErrorDiagnosticsQuery("24h", "all", _options.ErrorLimit),
            cancellationToken);

        if (!result.IsSuccess)
        {
            logger.LogWarning(
                "MR SAAS'y Bug Radar publishing skipped because the Workslip diagnostics query did not complete. Status={Status}.",
                result.Status);
            return;
        }

        var dashboard = result.Value;
        if (!MrSaasyBugRadarCheckpointPublisher.IsPublishable(dashboard))
        {
            logger.LogWarning(
                "MR SAAS'y Bug Radar publishing skipped because the diagnostics snapshot is not authoritative. Available={Available}; Complete={Complete}; Stale={Stale}; ItemsAvailable={ItemsAvailable}; Reason={Reason}.",
                dashboard.IsAvailable,
                dashboard.IsComplete,
                dashboard.IsStale,
                dashboard.ItemsAvailable,
                dashboard.AvailabilityReason);
            return;
        }

        var published = await publisher.PublishAsync(dashboard, cancellationToken);
        if (dashboard.IsTruncated)
        {
            logger.LogWarning(
                "MR SAAS'y Bug Radar published {PublishedCount} sanitized error signatures from a truncated Workslip diagnostics snapshot. No absent error was treated as resolved.",
                published);
            return;
        }

        logger.LogInformation(
            "MR SAAS'y Bug Radar published {PublishedCount} sanitized Workslip error signatures.",
            published);
    }
}
