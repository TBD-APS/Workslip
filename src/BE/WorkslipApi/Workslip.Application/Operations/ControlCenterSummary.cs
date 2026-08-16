namespace Workslip.Application.Operations;

public enum ControlCenterOverallState
{
    Healthy,
    Attention,
    Blocked,
    Stale,
    Unknown
}

public sealed record ControlCenterSummary(
    DateTimeOffset GeneratedAt,
    ControlCenterOverallState OverallState,
    int ApplicationCount,
    int HealthySignals,
    int AttentionSignals,
    int BlockedSignals,
    int StaleSignals,
    int UnknownSignals,
    int ActiveAutomationRuns,
    int FailedAutomationRuns,
    int BlockedAutomationRuns,
    int StaleAutomationRuns,
    int UnknownAutomationRuns);

public static class ControlCenterSummaryProjection
{
    private static readonly HashSet<ControlCenterSignalKind> OperationalKinds =
    [
        ControlCenterSignalKind.Health,
        ControlCenterSignalKind.Readiness,
        ControlCenterSignalKind.Telemetry
    ];

    public static ControlCenterSummary FromSnapshot(ControlCenterSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var healthySignals = 0;
        var attentionSignals = 0;
        var blockedSignals = 0;
        var staleSignals = 0;
        var unknownSignals = 0;
        var activeAutomationRuns = 0;
        var failedAutomationRuns = 0;
        var blockedAutomationRuns = 0;
        var staleAutomationRuns = 0;
        var unknownAutomationRuns = 0;

        foreach (var application in snapshot.Applications)
        {
            foreach (var signal in application.OperationalSignals)
            {
                if (signal.IsStale(snapshot.GeneratedAt))
                {
                    staleSignals++;
                    continue;
                }

                switch (signal.State)
                {
                    case PlatformHealthState.Healthy:
                        healthySignals++;
                        break;
                    case PlatformHealthState.Degraded:
                    case PlatformHealthState.Unhealthy:
                        attentionSignals++;
                        break;
                    case PlatformHealthState.Blocked:
                        blockedSignals++;
                        break;
                    case PlatformHealthState.Unknown:
                        unknownSignals++;
                        break;
                }
            }

            foreach (var source in application.Application.Sources.Where(source => OperationalKinds.Contains(source.Kind)))
            {
                var observed = application.OperationalSignals.Any(signal =>
                    signal.Kind == source.Kind &&
                    string.Equals(
                        signal.Evidence.Provider,
                        source.Evidence.Provider,
                        StringComparison.OrdinalIgnoreCase));

                if (!observed)
                {
                    unknownSignals++;
                }
            }

            foreach (var run in application.AutomationRuns)
            {
                switch (run.State)
                {
                    case AutomationRunState.Running:
                        activeAutomationRuns++;
                        break;
                    case AutomationRunState.Failed:
                        failedAutomationRuns++;
                        break;
                    case AutomationRunState.Blocked:
                        blockedAutomationRuns++;
                        break;
                    case AutomationRunState.Stale:
                        staleAutomationRuns++;
                        break;
                    case AutomationRunState.Unknown:
                        unknownAutomationRuns++;
                        break;
                }
            }
        }

        var overallState = GetOverallState(
            snapshot.Applications.Count,
            attentionSignals,
            blockedSignals,
            staleSignals,
            unknownSignals,
            failedAutomationRuns,
            blockedAutomationRuns,
            staleAutomationRuns,
            unknownAutomationRuns);

        return new ControlCenterSummary(
            snapshot.GeneratedAt,
            overallState,
            snapshot.Applications.Count,
            healthySignals,
            attentionSignals,
            blockedSignals,
            staleSignals,
            unknownSignals,
            activeAutomationRuns,
            failedAutomationRuns,
            blockedAutomationRuns,
            staleAutomationRuns,
            unknownAutomationRuns);
    }

    private static ControlCenterOverallState GetOverallState(
        int applicationCount,
        int attentionSignals,
        int blockedSignals,
        int staleSignals,
        int unknownSignals,
        int failedAutomationRuns,
        int blockedAutomationRuns,
        int staleAutomationRuns,
        int unknownAutomationRuns)
    {
        if (applicationCount == 0)
        {
            return ControlCenterOverallState.Unknown;
        }

        if (blockedSignals > 0 || blockedAutomationRuns > 0)
        {
            return ControlCenterOverallState.Blocked;
        }

        if (staleSignals > 0 || staleAutomationRuns > 0)
        {
            return ControlCenterOverallState.Stale;
        }

        if (attentionSignals > 0 || failedAutomationRuns > 0)
        {
            return ControlCenterOverallState.Attention;
        }

        if (unknownSignals > 0 || unknownAutomationRuns > 0)
        {
            return ControlCenterOverallState.Unknown;
        }

        return ControlCenterOverallState.Healthy;
    }
}
