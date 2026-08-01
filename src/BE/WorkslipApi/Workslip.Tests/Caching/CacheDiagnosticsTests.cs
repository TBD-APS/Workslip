using Workslip.Application.Common;

namespace Workslip.Tests.Caching;

public sealed class CacheDiagnosticsTests
{
    [Fact]
    public void Snapshot_exposes_aggregate_metrics_without_cache_keys_or_values()
    {
        var diagnostics = CreateDiagnostics();

        diagnostics.RecordHit(CacheRegionNames.ReferenceData);
        diagnostics.RecordMiss(CacheRegionNames.ReferenceData);
        diagnostics.RecordSet(CacheRegionNames.ReferenceData);
        diagnostics.RecordLoad(CacheRegionNames.ReferenceData, TimeSpan.FromMilliseconds(12.5));
        diagnostics.RecordFailure(CacheRegionNames.ReferenceData);

        var snapshot = diagnostics.GetSnapshot();
        var region = Assert.Single(snapshot.Regions, item => item.Name == CacheRegionNames.ReferenceData);

        Assert.Equal("HybridCache", region.Type);
        Assert.Equal(600, region.TtlSeconds);
        Assert.Equal(1, region.Hits);
        Assert.Equal(1, region.Misses);
        Assert.Equal(1, region.Sets);
        Assert.Equal(1, region.Failures);
        Assert.Equal(1, region.Loads);
        Assert.Equal(12.5, region.AverageLoadDurationMs, precision: 3);
        Assert.NotNull(region.LastActivityAt);
    }

    [Fact]
    public void Global_clear_marks_every_registered_region_invalidated()
    {
        var diagnostics = CreateDiagnostics();

        diagnostics.RecordGlobalClear();

        var snapshot = diagnostics.GetSnapshot();

        Assert.NotNull(snapshot.LastClearedAt);
        Assert.All(snapshot.Regions, region => Assert.Equal(1, region.Invalidations));
    }

    private static CacheDiagnostics CreateDiagnostics() => new(
    [
        new CacheRegionDefinition(CacheRegionNames.ReferenceData, "HybridCache", 600),
        new CacheRegionDefinition(CacheRegionNames.AuthenticatedUsers, "IMemoryCache", 3600)
    ]);
}
