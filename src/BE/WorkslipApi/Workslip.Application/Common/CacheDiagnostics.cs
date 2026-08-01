using System.Collections.Concurrent;

namespace Workslip.Application.Common;

public static class CacheRegionNames
{
    public const string ReferenceData = "reference-data";
    public const string AuthenticatedUsers = "authenticated-users";
}

public sealed record CacheRegionDefinition(
    string Name,
    string Type,
    int TtlSeconds);

public sealed record CacheRegionSnapshot(
    string Name,
    string Type,
    int TtlSeconds,
    long Hits,
    long Misses,
    long Sets,
    long Invalidations,
    long Failures,
    long Loads,
    double AverageLoadDurationMs,
    DateTimeOffset? LastActivityAt);

public sealed record CacheDiagnosticsSnapshot(
    string InstanceId,
    DateTimeOffset StartedAt,
    DateTimeOffset? LastClearedAt,
    IReadOnlyList<CacheRegionSnapshot> Regions);

public interface ICacheDiagnostics
{
    void RecordHit(string region);
    void RecordMiss(string region);
    void RecordSet(string region);
    void RecordInvalidation(string region);
    void RecordFailure(string region);
    void RecordLoad(string region, TimeSpan duration);
    void RecordGlobalClear();
    CacheDiagnosticsSnapshot GetSnapshot();
}

public sealed class CacheDiagnostics : ICacheDiagnostics
{
    private readonly ConcurrentDictionary<string, RegionState> _regions = new(StringComparer.Ordinal);
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private long _lastClearedAtUnixMilliseconds;

    public CacheDiagnostics(IEnumerable<CacheRegionDefinition> definitions)
    {
        foreach (var definition in definitions)
        {
            _regions.TryAdd(definition.Name, new RegionState(definition));
        }
    }

    public void RecordHit(string region) => Record(region, state => Interlocked.Increment(ref state.Hits));

    public void RecordMiss(string region) => Record(region, state => Interlocked.Increment(ref state.Misses));

    public void RecordSet(string region) => Record(region, state => Interlocked.Increment(ref state.Sets));

    public void RecordInvalidation(string region) => Record(region, state => Interlocked.Increment(ref state.Invalidations));

    public void RecordFailure(string region) => Record(region, state => Interlocked.Increment(ref state.Failures));

    public void RecordLoad(string region, TimeSpan duration)
    {
        Record(region, state =>
        {
            Interlocked.Increment(ref state.Loads);
            Interlocked.Add(ref state.TotalLoadDurationMicroseconds, (long)Math.Round(duration.TotalMilliseconds * 1_000));
        });
    }

    public void RecordGlobalClear()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Interlocked.Exchange(ref _lastClearedAtUnixMilliseconds, now);

        foreach (var state in _regions.Values)
        {
            Interlocked.Increment(ref state.Invalidations);
            Interlocked.Exchange(ref state.LastActivityUnixMilliseconds, now);
        }
    }

    public CacheDiagnosticsSnapshot GetSnapshot()
    {
        var lastClearedAt = Volatile.Read(ref _lastClearedAtUnixMilliseconds);
        var regions = _regions.Values
            .OrderBy(state => state.Definition.Name, StringComparer.Ordinal)
            .Select(ToSnapshot)
            .ToArray();

        return new CacheDiagnosticsSnapshot(
            _instanceId,
            _startedAt,
            lastClearedAt == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(lastClearedAt),
            regions);
    }

    private void Record(string region, Action<RegionState> update)
    {
        var state = _regions.GetOrAdd(
            region,
            name => new RegionState(new CacheRegionDefinition(name, "Unknown", 0)));

        update(state);
        Interlocked.Exchange(ref state.LastActivityUnixMilliseconds, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    private static CacheRegionSnapshot ToSnapshot(RegionState state)
    {
        var loads = Volatile.Read(ref state.Loads);
        var totalLoadDurationMicroseconds = Volatile.Read(ref state.TotalLoadDurationMicroseconds);
        var lastActivityAt = Volatile.Read(ref state.LastActivityUnixMilliseconds);

        return new CacheRegionSnapshot(
            state.Definition.Name,
            state.Definition.Type,
            state.Definition.TtlSeconds,
            Volatile.Read(ref state.Hits),
            Volatile.Read(ref state.Misses),
            Volatile.Read(ref state.Sets),
            Volatile.Read(ref state.Invalidations),
            Volatile.Read(ref state.Failures),
            loads,
            loads == 0 ? 0 : totalLoadDurationMicroseconds / 1_000d / loads,
            lastActivityAt == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(lastActivityAt));
    }

    private sealed class RegionState(CacheRegionDefinition definition)
    {
        public CacheRegionDefinition Definition { get; } = definition;
        public long Hits;
        public long Misses;
        public long Sets;
        public long Invalidations;
        public long Failures;
        public long Loads;
        public long TotalLoadDurationMicroseconds;
        public long LastActivityUnixMilliseconds;
    }
}
