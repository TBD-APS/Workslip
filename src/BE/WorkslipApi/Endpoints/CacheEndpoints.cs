using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Workslip.Application.Common;

namespace Workslip.Api.Endpoints;

public static class CacheEndpoints
{
    public static IEndpointRouteBuilder MapCacheEndpoints(this IEndpointRouteBuilder app)
    {
        var adminGroup = app.MapAdminGroup("/api/admin/cache", "cache");
        adminGroup.MapPost("/clear", ClearCachesAsync)
            .Produces<CacheClearResponse>();

        var superAdminGroup = app.MapSuperAdminGroup("/api/superadmin/cache", "cache");
        superAdminGroup.MapGet("/status", GetStatus)
            .Produces<CacheStatusResponse>();
        superAdminGroup.MapPost("/clear", ClearCachesAsync)
            .Produces<CacheClearResponse>();

        return app;
    }

    private static IResult GetStatus(
        HttpContext httpContext,
        ICacheDiagnostics cacheDiagnostics)
    {
        HttpCacheHeaders.SetNoStore(httpContext);

        return Results.Ok(new CacheStatusResponse(cacheDiagnostics.GetSnapshot()));
    }

    private static async Task<IResult> ClearCachesAsync(
        HttpContext httpContext,
        HybridCache hybridCache,
        IMemoryCache memoryCache,
        ICacheDiagnostics cacheDiagnostics,
        CancellationToken cancellationToken)
    {
        HttpCacheHeaders.SetNoStore(httpContext);

        // The clear covers every cache this process owns. The frontend is served
        // by nginx inside the app container with no shared cache in front of it,
        // so no remote invalidation call is part of this operation; the remaining
        // browser-side layers are cleared by the caller.
        await hybridCache.RemoveByTagAsync("all", cancellationToken);

        if (memoryCache is MemoryCache concrete)
        {
            concrete.Compact(1.0);
        }

        cacheDiagnostics.RecordGlobalClear();
        var snapshot = cacheDiagnostics.GetSnapshot();

        return Results.Ok(new CacheClearResponse(
            "All caches cleared.",
            snapshot.LastClearedAt ?? DateTimeOffset.UtcNow));
    }
}

public sealed record CacheStatusResponse(
    CacheDiagnosticsSnapshot Backend);

public sealed record CacheClearResponse(
    string Message,
    DateTimeOffset ClearedAt);
