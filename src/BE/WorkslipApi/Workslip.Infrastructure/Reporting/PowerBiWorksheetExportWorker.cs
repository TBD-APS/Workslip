using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Workslip.Application.Worksheets;

namespace Workslip.Infrastructure.Reporting;

public sealed class PowerBiWorksheetExportWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<PowerBiExportOptions> options,
    ILogger<PowerBiWorksheetExportWorker> logger) : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);
    private readonly PowerBiExportOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Power BI worksheet export is disabled.");
            return;
        }

        ValidateOptions();

        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
            var interval = TimeSpan.FromMinutes(_options.RefreshIntervalMinutes);
            using var timer = new PeriodicTimer(interval);

            do
            {
                try
                {
                    await ExportAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Power BI worksheet export failed. Next attempt in {RetryInterval}.",
                        interval);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task ExportAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var worksheets = scope.ServiceProvider.GetRequiredService<IWorksheetRepository>();
        var storage = scope.ServiceProvider.GetRequiredService<IPowerBiWorksheetExportStorage>();
        var scopeResolver = scope.ServiceProvider.GetRequiredService<PowerBiWorksheetExportScopeResolver>();

        var normalizedEmail = _options.ReaderEmail.Trim().ToLowerInvariant();
        var normalizedEntraObjectId = _options.ReaderEntraObjectId.Trim();
        var organizationId = await scopeResolver.ResolveOrganizationIdAsync(
            normalizedEmail,
            normalizedEntraObjectId,
            cancellationToken);
        if (organizationId is null)
        {
            logger.LogError(
                "Power BI worksheet export requires exactly one Workslip organization for the configured Entra identity.");
            return;
        }

        var currentMonth = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var firstMonth = currentMonth.AddMonths(-(_options.HistoryMonths - 1));
        var entries = new List<MyWorksheetEntryResponse>();

        for (var month = firstMonth; month <= currentMonth; month = month.AddMonths(1))
        {
            var monthEnd = month.AddMonths(1).AddDays(-1);
            entries.AddRange(await worksheets.GetAllWorksheetsAsync(
                organizationId.Value,
                month,
                monthEnd,
                cancellationToken));
        }

        var exportedAtUtc = DateTimeOffset.UtcNow;
        var content = PowerBiWorksheetCsvSerializer.Serialize(entries, exportedAtUtc);
        await storage.UploadAsync(content, exportedAtUtc, cancellationToken);

        logger.LogInformation(
            "Power BI worksheet export completed. RowCount: {RowCount}; HistoryMonths: {HistoryMonths}.",
            entries.Count,
            _options.HistoryMonths);
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.ReaderEmail))
        {
            throw new InvalidOperationException("PowerBiExport:ReaderEmail is required when export is enabled.");
        }

        if (!Guid.TryParse(_options.ReaderEntraObjectId, out var readerObjectId))
        {
            throw new InvalidOperationException(
                "PowerBiExport:ReaderEntraObjectId must be a valid Microsoft Entra object ID when export is enabled.");
        }

        var expectedContainerName = $"powerbi-{readerObjectId:N}"[..20];
        if (!string.Equals(_options.ContainerName, expectedContainerName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "PowerBiExport:ContainerName does not match the configured Microsoft Entra reader identity.");
        }

        if (_options.HistoryMonths is < 1 or > 120)
        {
            throw new InvalidOperationException("PowerBiExport:HistoryMonths must be between 1 and 120.");
        }

        if (_options.RefreshIntervalMinutes is < 15 or > 1440)
        {
            throw new InvalidOperationException(
                "PowerBiExport:RefreshIntervalMinutes must be between 15 and 1440.");
        }
    }
}
