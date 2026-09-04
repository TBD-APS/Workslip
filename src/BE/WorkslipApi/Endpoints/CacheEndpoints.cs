using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Workslip.Application.Common;
using Workslip.Infrastructure.Diagnostics;

namespace Workslip.Api.Endpoints;

public static class CacheEndpoints
{
    public static IEndpointRouteBuilder MapCacheEndpoints(this IEndpointRouteBuilder app)
    {
        var adminGroup = app.MapAdminGroup("/api/admin/cache", "cache");
        adminGroup.MapPost("/clear", ClearCachesAsync)
            .Produces<CacheClearResponse>();

        var superAdminGroup = app.MapSuperAdminGroup("/api/superadmin/cache", "cache");
        superAdminGroup.MapGet("/status", GetStatusAsync)
            .Produces<CacheStatusResponse>();
        superAdminGroup.MapPost("/clear", ClearCachesAsync)
            .Produces<CacheClearResponse>();

        return app;
    }

    private static async Task<IResult> GetStatusAsync(
        HttpContext httpContext,
        ICacheDiagnostics cacheDiagnostics,
        // [FromServices] rather than inferred: a collection-typed parameter is the
        // one shape where minimal-API binding could plausibly read it from the body.
        [FromServices] IReadOnlyList<CacheRegionDefinition> cacheRegions,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        HttpCacheHeaders.SetNoStore(httpContext);

        // Resolved from the request container rather than declared as a parameter:
        // the distributed cache is configured-or-not, and the diagnostics must work
        // in both shapes without a registration of its own.
        var distributed = await DistributedCacheProbe.ProbeAsync(
            httpContext.RequestServices.GetService<IDistributedCache>(),
            timeProvider,
            cancellationToken);

        var snapshot = CacheReach.Describe(cacheDiagnostics.GetSnapshot(), distributed, cacheRegions);

        return Results.Ok(new CacheStatusResponse(
            snapshot,
            distributed,
            CacheReach.WidestClearScope(snapshot),
            CacheReach.ClearReachesEveryReplica));
    }

    private static async Task<IResult> ClearCachesAsync(
        HttpContext httpContext,
        HybridCache hybridCache,
        IMemoryCache memoryCache,
        ICacheDiagnostics cacheDiagnostics,
        [FromServices] IReadOnlyList<CacheRegionDefinition> cacheRegions,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        HttpCacheHeaders.SetNoStore(httpContext);

        var distributedCache = httpContext.RequestServices.GetService<IDistributedCache>();
        Exception? distributedFailure = null;

        // RemoveByTagAsync invalidates the tag in this process first and only then
        // writes the shared marker, and it rethrows when that write fails. The local
        // clear therefore survives an unreachable L2, so a cache outage must not turn
        // an administrative clear into a 500: it degrades to a process-local clear
        // that says so. A failure with no distributed cache configured is a genuine
        // local fault and still surfaces.
        try
        {
            await hybridCache.RemoveByTagAsync(CacheTagNames.All, cancellationToken);
        }
        catch (Exception exception) when (distributedCache is not null && exception is not OperationCanceledException)
        {
            distributedFailure = exception;

            // The category, never the exception: RemoveByTagAsync fails on the write of
            // the shared marker, and the provider message that comes back names both the
            // endpoint and the marker key - measured, with an unreachable Redis:
            // "No connection is active/available to service this operation:
            //  HMSET workslip:development:__MSFT_HCT__all; UnableToConnect on 127.0.0.1:1/…".
            loggerFactory
                .CreateLogger("CacheAdministration")
                .LogWarning(
                    "Distributed cache tier could not be marked invalid; the local caches were cleared anyway. Cache failure: {CacheFailure}.",
                    DistributedCacheProbe.DescribeFailureForLog(exception));
        }

        // HybridCache's L1 is the registered IMemoryCache, so this also drops the
        // hybrid entries this process holds, plus the regions that use IMemoryCache
        // directly and carry no tags at all.
        if (memoryCache is MemoryCache concrete)
        {
            concrete.Compact(1.0);
        }

        cacheDiagnostics.RecordGlobalClear();

        var distributed = DistributedCacheProbe.FromOutcome(distributedCache, distributedFailure, timeProvider);
        var snapshot = CacheReach.Describe(cacheDiagnostics.GetSnapshot(), distributed, cacheRegions);
        var distributedTierCleared = distributedCache is not null && distributedFailure is null;

        return Results.Ok(new CacheClearResponse(
            CacheReach.DescribeClear(snapshot.InstanceId, distributed, distributedTierCleared),
            snapshot.LastClearedAt ?? timeProvider.GetUtcNow(),
            snapshot.InstanceId,
            CacheReach.WidestClearScope(snapshot),
            CacheReach.ClearReachesEveryReplica,
            distributedTierCleared,
            distributed));
    }
}

public sealed record CacheStatusResponse(
    CacheDiagnosticsSnapshot Backend,
    DistributedCacheSnapshot Distributed,
    CacheClearScope ClearScope,
    bool ClearReachesEveryReplica);

public sealed record CacheClearResponse(
    string Message,
    DateTimeOffset ClearedAt,
    string InstanceId,
    CacheClearScope Scope,
    bool ReachedEveryReplica,
    bool DistributedTierCleared,
    DistributedCacheSnapshot Distributed);
