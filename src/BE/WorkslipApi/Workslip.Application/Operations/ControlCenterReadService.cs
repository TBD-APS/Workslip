using Ardalis.Result;

namespace Workslip.Application.Operations;

public sealed class ControlCenterReadService(
    IApplicationEnvironmentRegistry registry,
    IEnumerable<IAutomationRunProvider> automationProviders,
    TimeProvider timeProvider) : IControlCenterReadService
{
    private static readonly TimeSpan ProviderFreshnessWindow = TimeSpan.FromMinutes(5);

    public async Task<Result<ControlCenterSnapshot>> GetSnapshotAsync(
        CancellationToken cancellationToken)
    {
        var applications = await registry.ListAsync(cancellationToken);
        var snapshots = new List<ControlCenterApplicationSnapshot>(applications.Count);

        foreach (var application in applications)
        {
            var operationalSignals = new List<ObservedSignal<PlatformHealthState>>();
            var automationRuns = new List<AutomationRunSummary>();

            foreach (var provider in automationProviders)
            {
                var source = application.Sources.FirstOrDefault(item =>
                    item.Kind == ControlCenterSignalKind.Automation
                    && string.Equals(
                        item.Evidence.Provider,
                        provider.Provider,
                        StringComparison.OrdinalIgnoreCase));
                if (source is null)
                {
                    continue;
                }

                var observedAt = timeProvider.GetUtcNow();
                try
                {
                    var result = await provider.ListRunsAsync(application, cancellationToken);
                    if (result.IsSuccess)
                    {
                        automationRuns.AddRange(result.Value);
                        operationalSignals.Add(new ObservedSignal<PlatformHealthState>(
                            ControlCenterSignalKind.Automation,
                            PlatformHealthState.Healthy,
                            observedAt,
                            observedAt.Add(ProviderFreshnessWindow),
                            source.Evidence));
                    }
                    else
                    {
                        operationalSignals.Add(BlockedAutomationSignal(source, observedAt));
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    operationalSignals.Add(BlockedAutomationSignal(source, observedAt));
                }
            }

            snapshots.Add(new ControlCenterApplicationSnapshot(
                application,
                operationalSignals,
                automationRuns
                    .OrderByDescending(run => run.StartedAt)
                    .ToArray()));
        }

        return Result<ControlCenterSnapshot>.Success(new ControlCenterSnapshot(
            timeProvider.GetUtcNow(),
            snapshots));
    }

    private static ObservedSignal<PlatformHealthState> BlockedAutomationSignal(
        ControlCenterSourceRegistration source,
        DateTimeOffset observedAt) =>
        new(
            ControlCenterSignalKind.Automation,
            PlatformHealthState.Blocked,
            observedAt,
            observedAt,
            source.Evidence);
}
