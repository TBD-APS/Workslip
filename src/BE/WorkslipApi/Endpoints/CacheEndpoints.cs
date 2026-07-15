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
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            await hybridCache.RemoveByTagAsync("all", cancellationToken);

            if (memoryCache is MemoryCache concrete)
            {
                concrete.Compact(1.0);
            }

            var vercelProjectId = configuration["Vercel:projectId"];
            var vercelToken = configuration["Vercel:Token"];

            var isVercelCacheCleared = false;
            if (!string.IsNullOrEmpty(vercelToken) && !string.IsNullOrEmpty(vercelProjectId))
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", vercelToken);

                var response = await httpClient.PostAsJsonAsync(
                    $"https://api.vercel.com/v1/edge-cache/invalidate-by-tags?projectIdOrName={vercelProjectId}",
                    new { tags = new[] { "all" }, target = "production" },
                    cancellationToken);

                isVercelCacheCleared = response.IsSuccessStatusCode;
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    return Results.Ok(new { message = $"All caches cleared. Vercel CDN purge failed: {response.StatusCode} {body}" });
                }
            }

            return Results.Ok(new { message = $"All caches cleared. Vercel Cache cleared: {isVercelCacheCleared}" });
        });

        return app;
    }
}
