using System.Security.Cryptography;
using System.Text;
using Workslip.Application.Jobs;
using Workslip.Domain;

namespace Workslip.Api.Endpoints;

public static class HttpCacheHeaders
{
    private const string PrivateRevalidate = "private, no-cache, max-age=0, must-revalidate";
    private const string NoStore = "no-store";
    private const string PublicHealth = "public, max-age=30, stale-while-revalidate=30";

    public static void SetPrivateRevalidation(HttpContext httpContext, string etag)
    {
        httpContext.Response.Headers.CacheControl = PrivateRevalidate;
        httpContext.Response.Headers.ETag = etag;
        httpContext.Response.Headers.Vary = "Authorization";
    }

    public static void SetNoStore(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = NoStore;
        httpContext.Response.Headers.Pragma = "no-cache";
        httpContext.Response.Headers.Expires = "0";
    }

    public static void SetPublicHealthCache(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = PublicHealth;
    }

    public static bool MatchesIfNoneMatch(HttpContext httpContext, string etag)
    {
        if (!httpContext.Request.Headers.TryGetValue("If-None-Match", out var values))
        {
            return false;
        }

        return values.ToString()
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Any(value => value == "*" || string.Equals(value, etag, StringComparison.Ordinal));
    }

    public static string JobReportEtag(JobReportResponse report) => ToWeakEtag(
        $"job:{report.OrganizationId:N}:{report.Id:N}:{report.UpdatedAt.ToUnixTimeMilliseconds()}:{report.SubmittedAt?.ToUnixTimeMilliseconds() ?? 0}");

    public static string JobListEtag(
        IEnumerable<JobListItemResponse> jobs,
        Guid organizationId,
        JobStatus? status,
        string? reportNumber,
        string? customerName,
        string? customerEmail,
        string? customerAddress,
        int? limit,
        int? offset)
    {
        var builder = new StringBuilder()
            .Append("jobs:list:")
            .Append(organizationId.ToString("N"))
            .Append(':')
            .Append(status?.ToString() ?? "all")
            .Append(':')
            .Append(reportNumber?.ToLowerInvariant() ?? "none")
            .Append(':')
            .Append(customerName?.ToLowerInvariant() ?? "none")
            .Append(':')
            .Append(customerEmail?.ToLowerInvariant() ?? "none")
            .Append(':')
            .Append(customerAddress?.ToLowerInvariant() ?? "none")
            .Append(':')
            .Append(limit?.ToString() ?? "default")
            .Append(':')
            .Append(offset?.ToString() ?? "default");

        foreach (var job in jobs.OrderBy(job => job.Id))
        {
            builder
                .Append('|')
                .Append(job.Id.ToString("N"))
                .Append(':')
                .Append(job.UpdatedAt.ToUnixTimeMilliseconds())
                .Append(':')
                .Append(job.Status);
        }

        return ToWeakEtag(builder.ToString());
    }

    private static string ToWeakEtag(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"W/\"{Convert.ToHexString(hash)[..24].ToLowerInvariant()}\"";
    }
}
