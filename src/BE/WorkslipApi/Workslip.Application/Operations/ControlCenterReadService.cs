using Ardalis.Result;

namespace Workslip.Application.Operations;

public sealed class ControlCenterReadService(
    IApplicationEnvironmentRegistry registry,
    TimeProvider timeProvider) : IControlCenterReadService
{
    public async Task<Result<ControlCenterSnapshot>> GetSnapshotAsync(
        CancellationToken cancellationToken)
    {
        var applications = await registry.ListAsync(cancellationToken);
        var snapshots = applications
            .Select(application => new ControlCenterApplicationSnapshot(
                application,
                OperationalSignals: Array.Empty<ObservedSignal<PlatformHealthState>>(),
                AutomationRuns: Array.Empty<AutomationRunSummary>()))
            .ToArray();

        return Result<ControlCenterSnapshot>.Success(new ControlCenterSnapshot(
            timeProvider.GetUtcNow(),
            snapshots));
    }
}
