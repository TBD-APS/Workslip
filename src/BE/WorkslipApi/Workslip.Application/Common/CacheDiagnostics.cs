using System.Collections.Concurrent;

namespace Workslip.Application.Common;

public static class CacheRegionNames
{
    public const string ReferenceData = "reference-data";
    public const string AuthenticatedUsers = "authenticated-users";
}

/// <summary>
/// Tags every HybridCache consumer attaches to its entries. The administrative
/// clear can only reach entries carrying <see cref="All"/>, so the tag is part of
/// the cache contract rather than a local literal.
/// </summary>
public static class CacheTagNames
{
    public const string All = "all";
}

/// <summary>
/// The store backing a region, as registered with <see cref="CacheRegionDefinition"/>.
/// The value decides what a region <i>can</i> have: a HybridCache region is offered an
/// L2 as soon as an <c>IDistributedCache</c> is registered, while a raw
/// <c>IMemoryCache</c> region is process-local forever. Whether an offered L2 is
/// actually used is the region's <see cref="CacheEntryReach"/>, which is a separate
/// field for exactly that reason — reading the tier off this value alone is what made
/// the diagnostics screen overstate the authenticated-user region.
/// </summary>
public static class CacheStoreTypes
{
    public const string Hybrid = "HybridCache";
    public const string Memory = "IMemoryCache";
    public const string Unknown = "Unknown";
}

/// <summary>Which cache levels a region is actually served from right now.</summary>
public enum CacheTier
{
    /// <summary>In-process only: every replica keeps its own copy.</summary>
    LocalOnly,

    /// <summary>In-process L1 in front of a shared distributed L2.</summary>
    LocalAndDistributed
}

/// <summary>How far the administrative cache clear reaches for a region.</summary>
public enum CacheClearScope
{
    /// <summary>Only the API process that served the request.</summary>
    ProcessOnly,

    /// <summary>
    /// The API process that served the request, plus the shared distributed tier.
    /// Still not the in-process L1 of the other replicas — see
    /// <see cref="CacheReach.ClearReachesEveryReplica"/>.
    /// </summary>
    ProcessAndDistributedTier
}

/// <summary>Result of probing the registered distributed cache, if there is one.</summary>
public enum DistributedCacheState
{
    /// <summary>No <c>IDistributedCache</c> is registered; HybridCache runs L1-only.</summary>
    NotConfigured,

    /// <summary>The distributed cache answered a read within the probe timeout.</summary>
    Reachable,

    /// <summary>A distributed cache is registered but did not answer.</summary>
    Unreachable
}

public sealed record DistributedCacheSnapshot(
    bool Configured,
    DistributedCacheState State,
    string? Provider,
    string? Error,
    DateTimeOffset? CheckedAt)
{
    public static DistributedCacheSnapshot NotConfigured { get; } =
        new(false, DistributedCacheState.NotConfigured, null, null, null);
}

/// <summary>
/// A registered cache region. <paramref name="Type"/> names the store it is
/// registered against and <paramref name="EntryReach"/> says how far that store's
/// tiers are actually used by its entries; both are needed to describe the region
/// truthfully, because a HybridCache region whose calls disable the distributed cache
/// is registered against an L2 it never touches — see <see cref="CacheEntryReach"/>
/// and <see cref="CacheReach.TierFor"/>.
/// </summary>
public sealed record CacheRegionDefinition(
    string Name,
    string Type,
    int TtlSeconds,
    CacheEntryReach EntryReach = CacheEntryReach.StoreTiers);

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
    DateTimeOffset? LastActivityAt,
    CacheTier Tier,
    CacheClearScope ClearScope);

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

    /// <summary>
    /// The counters this process owns. Regions are reported at their process-local
    /// baseline (<see cref="CacheTier.LocalOnly"/> / <see cref="CacheClearScope.ProcessOnly"/>)
    /// because this type cannot see whether a distributed cache is registered;
    /// <see cref="CacheReach.Describe"/> re-derives both once the distributed tier
    /// has been probed.
    /// </summary>
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
            name => new RegionState(new CacheRegionDefinition(name, CacheStoreTypes.Unknown, 0)));

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
            lastActivityAt == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(lastActivityAt),
            CacheTier.LocalOnly,
            CacheClearScope.ProcessOnly);
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
