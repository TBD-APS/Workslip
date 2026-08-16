using Ardalis.Result;

namespace Workslip.Application.Operations;

public enum PlatformHealthState
{
    Healthy,
    Degraded,
    Unhealthy,
    Unknown,
    Blocked
}

public enum AutomationRunState
{
    Running,
    Succeeded,
    Failed,
    Cancelled,
    Blocked,
    Stale,
    Unknown
}

public enum ControlCenterSignalKind
{
    Health,
    Readiness,
    Telemetry,
    Automation,
    Release,
    Incident
}

public sealed record EvidenceReference(
    string Provider,
    string Reference,
    string? ExternalId = null);

public sealed record ApplicationEnvironmentKey(
    string ApplicationId,
    string Environment);

public sealed record ControlCenterSourceRegistration(
    ControlCenterSignalKind Kind,
    EvidenceReference Evidence);

public sealed record ApplicationEnvironmentRegistration(
    ApplicationEnvironmentKey Key,
    string DisplayName,
    IReadOnlyList<ControlCenterSourceRegistration> Sources);

public sealed record ObservedSignal<TState>(
    ControlCenterSignalKind Kind,
    TState State,
    DateTimeOffset ObservedAt,
    DateTimeOffset? FreshUntil,
    EvidenceReference Evidence)
    where TState : struct, Enum
{
    public bool IsStale(DateTimeOffset now) =>
        FreshUntil is DateTimeOffset freshUntil && now > freshUntil;
}

public sealed record AutomationRunSummary(
    string RunId,
    string Workflow,
    AutomationRunState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    DateTimeOffset LastUpdatedAt,
    int Attempt,
    string? Revision,
    string? PullRequest,
    string? Issue,
    EvidenceReference Evidence);

public sealed record ControlCenterApplicationSnapshot(
    ApplicationEnvironmentRegistration Application,
    IReadOnlyList<ObservedSignal<PlatformHealthState>> OperationalSignals,
    IReadOnlyList<AutomationRunSummary> AutomationRuns);

public sealed record ControlCenterSnapshot(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<ControlCenterApplicationSnapshot> Applications);

public interface IApplicationEnvironmentRegistry
{
    Task<IReadOnlyList<ApplicationEnvironmentRegistration>> ListAsync(
        CancellationToken cancellationToken);
}

public interface IAutomationRunProvider
{
    string Provider { get; }

    Task<Result<IReadOnlyList<AutomationRunSummary>>> ListRunsAsync(
        ApplicationEnvironmentRegistration application,
        CancellationToken cancellationToken);
}

public interface IControlCenterReadService
{
    Task<Result<ControlCenterSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken);
}
