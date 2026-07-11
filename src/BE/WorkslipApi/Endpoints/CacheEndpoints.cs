using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;

namespace Workslip.Api.Endpoints;

public static class CacheEndpoints
{
    public static IEndpointRouteBuilder MapCacheEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapAdminGroup("/api/admin/cache", "cache");

        group.MapPost("/clear", async (
            HybridCache hybridCache,
            IMemoryCache memoryCache,
            CancellationToken cancellationToken) =>
        {
            await hybridCache.RemoveByTagAsync("all", cancellationToken);

            if (memoryCache is MemoryCache concrete)
            {
                concrete.Compact(1.0);
            }

            return Results.Ok(new { message = "All caches cleared." });
        });

        return app;
    }
}
