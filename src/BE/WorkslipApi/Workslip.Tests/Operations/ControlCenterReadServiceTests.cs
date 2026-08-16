using Workslip.Application.Operations;
using Xunit;

namespace Workslip.Tests.Operations;

public sealed class ControlCenterReadServiceTests
{
    [Fact]
    public async Task GetSnapshotAsync_preserves_registered_application_and_evidence_sources()
    {
        var now = new DateTimeOffset(2026, 8, 15, 19, 0, 0, TimeSpan.Zero);
        var registration = new ApplicationEnvironmentRegistration(
            new ApplicationEnvironmentKey("workslip", "production"),
            "Workslip",
            [
                new ControlCenterSourceRegistration(
                    ControlCenterSignalKind.Health,
                    new EvidenceReference("workslip-api", "/health")),
                new ControlCenterSourceRegistration(
                    ControlCenterSignalKind.Automation,
                    new EvidenceReference("github-actions", "rasm105k/Workslip-v2.0"))
            ]);
        var service = new ControlCenterReadService(
            new StubRegistry([registration]),
            new FixedTimeProvider(now));

        var result = await service.GetSnapshotAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(now, result.Value.GeneratedAt);
        var application = Assert.Single(result.Value.Applications);
        Assert.Equal("workslip", application.Application.Key.ApplicationId);
        Assert.Equal("production", application.Application.Key.Environment);
        Assert.Equal(2, application.Application.Sources.Count);
        Assert.Empty(application.OperationalSignals);
        Assert.Empty(application.AutomationRuns);
    }

    [Fact]
    public void ObservedSignal_is_stale_only_after_its_freshness_boundary()
    {
        var observedAt = new DateTimeOffset(2026, 8, 15, 19, 0, 0, TimeSpan.Zero);
        var freshUntil = observedAt.AddMinutes(5);
        var signal = new ObservedSignal<PlatformHealthState>(
            ControlCenterSignalKind.Health,
            PlatformHealthState.Healthy,
            observedAt,
            freshUntil,
            new EvidenceReference("fixture", "health://fixture"));

        Assert.Equal(ControlCenterSignalKind.Health, signal.Kind);
        Assert.False(signal.IsStale(freshUntil));
        Assert.True(signal.IsStale(freshUntil.AddTicks(1)));
    }

    private sealed class StubRegistry(IReadOnlyList<ApplicationEnvironmentRegistration> registrations)
        : IApplicationEnvironmentRegistry
    {
        public Task<IReadOnlyList<ApplicationEnvironmentRegistration>> ListAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(registrations);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
