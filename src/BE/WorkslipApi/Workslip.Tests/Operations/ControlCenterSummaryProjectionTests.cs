using Workslip.Application.Operations;
using Xunit;

namespace Workslip.Tests.Operations;

public sealed class ControlCenterSummaryProjectionTests
{
    [Fact]
    public void FromSnapshot_marks_missing_registered_operational_evidence_as_unknown()
    {
        var now = new DateTimeOffset(2026, 8, 16, 5, 0, 0, TimeSpan.Zero);
        var registration = new ApplicationEnvironmentRegistration(
            new ApplicationEnvironmentKey("workslip", "production"),
            "Workslip",
            [
                new ControlCenterSourceRegistration(
                    ControlCenterSignalKind.Health,
                    new EvidenceReference("workslip-api", "/health")),
                new ControlCenterSourceRegistration(
                    ControlCenterSignalKind.Readiness,
                    new EvidenceReference("workslip-api", "/readiness"))
            ]);
        var snapshot = new ControlCenterSnapshot(
            now,
            [
                new ControlCenterApplicationSnapshot(
                    registration,
                    [
                        new ObservedSignal<PlatformHealthState>(
                            ControlCenterSignalKind.Health,
                            PlatformHealthState.Healthy,
                            now.AddMinutes(-1),
                            now.AddMinutes(4),
                            new EvidenceReference("workslip-api", "/health"))
                    ],
                    Array.Empty<AutomationRunSummary>())
            ]);

        var summary = ControlCenterSummaryProjection.FromSnapshot(snapshot);

        Assert.Equal(ControlCenterOverallState.Unknown, summary.OverallState);
        Assert.Equal(1, summary.ApplicationCount);
        Assert.Equal(1, summary.HealthySignals);
        Assert.Equal(1, summary.UnknownSignals);
    }

    [Fact]
    public void FromSnapshot_counts_problem_states_and_prioritizes_blocked_company_state()
    {
        var now = new DateTimeOffset(2026, 8, 16, 5, 0, 0, TimeSpan.Zero);
        var registration = new ApplicationEnvironmentRegistration(
            new ApplicationEnvironmentKey("workslip", "production"),
            "Workslip",
            []);
        var snapshot = new ControlCenterSnapshot(
            now,
            [
                new ControlCenterApplicationSnapshot(
                    registration,
                    [
                        Signal(ControlCenterSignalKind.Health, PlatformHealthState.Degraded, now, fresh: true),
                        Signal(ControlCenterSignalKind.Readiness, PlatformHealthState.Healthy, now, fresh: false),
                        Signal(ControlCenterSignalKind.Telemetry, PlatformHealthState.Blocked, now, fresh: true)
                    ],
                    [
                        Run("running", AutomationRunState.Running, now),
                        Run("failed", AutomationRunState.Failed, now),
                        Run("blocked", AutomationRunState.Blocked, now),
                        Run("stale", AutomationRunState.Stale, now),
                        Run("unknown", AutomationRunState.Unknown, now)
                    ])
            ]);

        var summary = ControlCenterSummaryProjection.FromSnapshot(snapshot);

        Assert.Equal(ControlCenterOverallState.Blocked, summary.OverallState);
        Assert.Equal(1, summary.AttentionSignals);
        Assert.Equal(1, summary.BlockedSignals);
        Assert.Equal(1, summary.StaleSignals);
        Assert.Equal(1, summary.ActiveAutomationRuns);
        Assert.Equal(1, summary.FailedAutomationRuns);
        Assert.Equal(1, summary.BlockedAutomationRuns);
        Assert.Equal(1, summary.StaleAutomationRuns);
        Assert.Equal(1, summary.UnknownAutomationRuns);
    }

    [Fact]
    public void FromSnapshot_with_no_registered_applications_is_unknown()
    {
        var summary = ControlCenterSummaryProjection.FromSnapshot(
            new ControlCenterSnapshot(DateTimeOffset.UtcNow, []));

        Assert.Equal(ControlCenterOverallState.Unknown, summary.OverallState);
        Assert.Equal(0, summary.ApplicationCount);
    }

    private static ObservedSignal<PlatformHealthState> Signal(
        ControlCenterSignalKind kind,
        PlatformHealthState state,
        DateTimeOffset now,
        bool fresh) =>
        new(
            kind,
            state,
            now.AddMinutes(-2),
            fresh ? now.AddMinutes(3) : now.AddMinutes(-1),
            new EvidenceReference("fixture", $"fixture://{kind}"));

    private static AutomationRunSummary Run(
        string id,
        AutomationRunState state,
        DateTimeOffset now) =>
        new(
            id,
            "ci",
            state,
            now.AddMinutes(-10),
            null,
            now.AddMinutes(-1),
            1,
            "abc123",
            null,
            null,
            new EvidenceReference("fixture", $"fixture://runs/{id}"));
}
