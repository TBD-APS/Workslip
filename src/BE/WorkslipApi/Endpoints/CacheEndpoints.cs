using System.Net.Http.Headers;
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
        ICacheDiagnostics cacheDiagnostics,
        IConfiguration configuration)
    {
        HttpCacheHeaders.SetNoStore(httpContext);

        var vercelConfigured = HasVercelConfiguration(configuration);
        return Results.Ok(new CacheStatusResponse(
            cacheDiagnostics.GetSnapshot(),
            vercelConfigured));
    }

    private static async Task<IResult> ClearCachesAsync(
        HttpContext httpContext,
        HybridCache hybridCache,
        IMemoryCache memoryCache,
        ICacheDiagnostics cacheDiagnostics,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        HttpCacheHeaders.SetNoStore(httpContext);

        await hybridCache.RemoveByTagAsync("all", cancellationToken);

        if (memoryCache is MemoryCache concrete)
        {
            concrete.Compact(1.0);
        }

        var vercelProjectId = configuration["Vercel:ProjectId"];
        var vercelToken = configuration["Vercel:Token"];
        var vercelConfigured = !string.IsNullOrWhiteSpace(vercelProjectId)
            && !string.IsNullOrWhiteSpace(vercelToken);
        var vercelCleared = false;
        string? warning = null;

        if (vercelConfigured)
        {
            var httpClient = httpClientFactory.CreateClient("vercel-cache");
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://api.vercel.com/v1/edge-cache/invalidate-by-tags?projectIdOrName={Uri.EscapeDataString(vercelProjectId!)}")
            {
                Content = JsonContent.Create(new { tags = new[] { "all" }, target = "production" })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", vercelToken);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            vercelCleared = response.IsSuccessStatusCode;

            if (!vercelCleared)
            {
                warning = $"Vercel cache purge failed with status {(int)response.StatusCode}.";
                loggerFactory.CreateLogger("CacheAdministration")
                    .LogWarning(
                        "Vercel cache purge failed with status {StatusCode}.",
                        (int)response.StatusCode);
            }
        }

        cacheDiagnostics.RecordGlobalClear();
        var snapshot = cacheDiagnostics.GetSnapshot();

        return Results.Ok(new CacheClearResponse(
            snapshot.LastClearedAt ?? DateTimeOffset.UtcNow,
            vercelConfigured,
            vercelCleared,
            warning));
    }

    private static bool HasVercelConfiguration(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["Vercel:ProjectId"])
        && !string.IsNullOrWhiteSpace(configuration["Vercel:Token"]);
}

public sealed record CacheStatusResponse(
    CacheDiagnosticsSnapshot Backend,
    bool VercelConfigured);

public sealed record CacheClearResponse(
    DateTimeOffset ClearedAt,
    bool VercelConfigured,
    bool VercelCleared,
    string? Warning);
