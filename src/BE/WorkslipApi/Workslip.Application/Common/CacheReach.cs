namespace Workslip.Application.Common;

/// <summary>
/// How far a region's entries are allowed to travel, which is a separate question
/// from which store they are registered against. Declared on
/// <see cref="CacheRegionDefinition"/> because the store cannot answer it: a
/// HybridCache region whose call sites set
/// <c>HybridCacheEntryFlags.DisableDistributedCache</c> is registered against a store
/// that has an L2 and never uses it, and reading the tier off the store type alone is
/// what made the diagnostics screen report a shared tier for the authenticated-user
/// region on a Redis deployment.
/// </summary>
public enum CacheEntryReach
{
    /// <summary>
    /// Entries use every tier the store has. The default, because a region that says
    /// nothing about its entries is served by whatever its store is.
    /// </summary>
    StoreTiers,

    /// <summary>
    /// Entries opt out of the shared tier at every call site, so the region is
    /// process-local however the store is registered.
    /// </summary>
    ProcessLocal
}

/// <summary>
/// Answers, from the shape of the running configuration, how far a cache read and
/// an administrative cache clear actually reach. It exists so the diagnostics
/// endpoint and the superadmin screen never claim more than the code can deliver.
///
/// Verified against the installed Microsoft.Extensions.Caching.Hybrid package
/// (10.6.0 in Workslip.Api, which is the version the whole app loads; the 10.1.0
/// pins in Workslip.Application and Workslip.Infrastructure are floors that NuGet
/// unifies upwards, and their tag-invalidation internals are the same):
///
///   * <c>RemoveByTagAsync(tag)</c> invalidates the tag in the calling process and
///     writes an invalidation timestamp to L2 under <c>__MSFT_HCT__{tag}</c>. It does
///     not delete the L2 payloads and it has no backplane.
///   * A process reads that timestamp from L2 at most once per tag — lazily, the
///     first time it sees the tag — and memoises the answer for its lifetime in
///     <c>DefaultHybridCache._tagInvalidationTimes</c>, a
///     <c>ConcurrentDictionary&lt;string, Task&lt;long&gt;&gt;</c> with no expiry and no
///     refresh timer. So a replica that had already touched the tag keeps serving
///     its stale L1 entry, and keeps accepting the stale L2 payload after that L1
///     entry expires.
///   * Only a process that starts after the clear reads the marker and discards the
///     stale L2 payload.
///
/// Consequence: registering an L2 does not make the clear deployment-wide. No clear
/// reaches another running replica's L1, which is why
/// <see cref="ClearReachesEveryReplica"/> is false. Changing that needs a backplane
/// (for example Redis pub/sub) that tells every replica to drop its L1, not another
/// cache registration.
/// </summary>
public static class CacheReach
{
    /// <summary>
    /// False in every supported configuration. A clear invalidates the serving
    /// process and, when an L2 is configured, marks the shared tier; the other
    /// replicas keep their own in-process entries, and because the marker does not
    /// delete the shared payloads they can reload one when a local entry expires —
    /// so only a restart converges them. The characterization test in
    /// <c>CacheDiagnosticsTests</c> fails if a package upgrade ever changes that,
    /// which is the signal to revisit this constant.
    /// </summary>
    public const bool ClearReachesEveryReplica = false;

    /// <summary>
    /// Which levels a region is served from. This follows configuration, not
    /// reachability, on purpose: a registered L2 that is momentarily unreachable is
    /// still the topology the region runs in, and the distributed snapshot on the
    /// same response reports whether it answered. Contrast
    /// <see cref="ClearScopeFor"/>, which is a prediction about a clear the operator
    /// is about to press and therefore has to follow reachability.
    ///
    /// <paramref name="entryReach"/> is the other half of the question and the store
    /// type cannot answer it — see <see cref="CacheEntryReach"/>.
    /// </summary>
    public static CacheTier TierFor(
        string regionType,
        CacheEntryReach entryReach,
        DistributedCacheSnapshot distributed) =>
        UsesDistributedTier(regionType, entryReach) && distributed.Configured
            ? CacheTier.LocalAndDistributed
            : CacheTier.LocalOnly;

    /// <summary>
    /// How far a clear pressed right now would reach. A registered but unreachable
    /// L2 cannot be marked invalid, so the clear would reach this process only —
    /// reporting <see cref="CacheClearScope.ProcessAndDistributedTier"/> off
    /// <see cref="DistributedCacheSnapshot.Configured"/> alone promised the operator
    /// a shared-tier clear the code could not deliver, in the same payload that said
    /// <see cref="DistributedCacheState.Unreachable"/>.
    /// </summary>
    public static CacheClearScope ClearScopeFor(
        string regionType,
        CacheEntryReach entryReach,
        DistributedCacheSnapshot distributed) =>
        UsesDistributedTier(regionType, entryReach) && distributed.State == DistributedCacheState.Reachable
            ? CacheClearScope.ProcessAndDistributedTier
            : CacheClearScope.ProcessOnly;

    /// <summary>
    /// Restates a process-local snapshot with the tier and clear scope each region
    /// really has, given the probed distributed cache and what each region declared
    /// about its entries.
    /// </summary>
    /// <param name="regions">
    /// The registered region definitions, which are where <see cref="CacheEntryReach"/>
    /// is declared. A region present in the snapshot but not here was created at
    /// runtime by a <c>Record*</c> call rather than registered
    /// (<c>CacheDiagnostics.Record</c> adds it with <c>CacheStoreTypes.Unknown</c>),
    /// and is described from its store type alone.
    /// </param>
    public static CacheDiagnosticsSnapshot Describe(
        CacheDiagnosticsSnapshot snapshot,
        DistributedCacheSnapshot distributed,
        IReadOnlyList<CacheRegionDefinition> regions)
    {
        // TryAdd rather than ToDictionary: CacheDiagnostics tolerates a duplicated
        // region name in its own registration the same way, and a diagnostics call
        // must not be the thing that throws over one.
        var declaredReach = new Dictionary<string, CacheEntryReach>(StringComparer.Ordinal);
        foreach (var region in regions)
        {
            declaredReach.TryAdd(region.Name, region.EntryReach);
        }

        return snapshot with
        {
            Regions = snapshot.Regions
                .Select(region =>
                {
                    var entryReach = declaredReach.TryGetValue(region.Name, out var declared)
                        ? declared
                        : CacheEntryReach.StoreTiers;

                    return region with
                    {
                        Tier = TierFor(region.Type, entryReach, distributed),
                        ClearScope = ClearScopeFor(region.Type, entryReach, distributed)
                    };
                })
                .ToArray()
        };
    }

    /// <summary>The widest scope any single region gets from one clear.</summary>
    public static CacheClearScope WidestClearScope(CacheDiagnosticsSnapshot snapshot) =>
        snapshot.Regions.Any(region => region.ClearScope == CacheClearScope.ProcessAndDistributedTier)
            ? CacheClearScope.ProcessAndDistributedTier
            : CacheClearScope.ProcessOnly;

    /// <summary>
    /// The message the clear endpoint returns. Deliberately states what was not
    /// reached: the previous wording ("All caches cleared.") told an operator the
    /// whole deployment had been cleared, which was never true with more than one
    /// replica.
    /// </summary>
    public static string DescribeClear(
        string instanceId,
        DistributedCacheSnapshot distributed,
        bool distributedTierCleared)
    {
        var cleared = $"Cleared every cache owned by API instance {instanceId}.";

        if (!distributed.Configured)
        {
            return cleared
                + " No distributed cache is configured, so this is the only process affected:"
                + " every other replica keeps its own copy until it expires.";
        }

        if (!distributedTierCleared)
        {
            return cleared
                + " The distributed tier could not be marked invalid and still serves its cached"
                + " payloads to processes that read it.";
        }

        // "until those expire" was the earlier wording here, and it implied a
        // convergence that does not happen: the tag marker does not delete the L2
        // payloads, and a replica that has already read this tag has memoised its
        // invalidation timestamp for the life of the process, so when its local entry
        // expires it reloads the payload the clear left behind. Only a replica that
        // starts after the clear reads the marker and discards it.
        return cleared
            + " The distributed tier is marked invalid, so processes that start from now on"
            + " discard what they find there. The marker does not delete the shared payloads,"
            + " so a replica that is already running keeps serving its own copy and can reload"
            + " the shared one when that copy expires; only a restart converges it.";
    }

    /// <summary>
    /// Both halves have to hold: the store must have a shared tier to offer, and the
    /// region's entries must actually use it. The store half alone is what the
    /// authenticated-user region was reported on — it is registered against
    /// HybridCache, which is true, while every one of its calls sets
    /// <c>DisableDistributedCacheRead | DisableDistributedCacheWrite</c>.
    /// </summary>
    private static bool UsesDistributedTier(string regionType, CacheEntryReach entryReach) =>
        entryReach == CacheEntryReach.StoreTiers
        && string.Equals(regionType, CacheStoreTypes.Hybrid, StringComparison.Ordinal);
}
