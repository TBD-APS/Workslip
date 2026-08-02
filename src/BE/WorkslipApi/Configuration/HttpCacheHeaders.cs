using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Workslip.Api.ViewModels;
using Workslip.Application.Jobs;

namespace Workslip.Api.Endpoints;

public static class HttpCacheHeaders
{
    private const string PrivateRevalidate = "private, no-cache, max-age=0, must-revalidate";
    private const string NoStore = "no-store";
    private const string PublicHealth = "public, max-age=30, stale-while-revalidate=30";
    private const string CacheStatusHeader = "X-Workslip-Cache";

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
        httpContext.Response.Headers[CacheStatusHeader] = "bypass";
    }

    public static void SetPublicHealthCache(HttpContext httpContext)
    {
        httpContext.Response.Headers.CacheControl = PublicHealth;
    }

    public static bool MatchesIfNoneMatch(HttpContext httpContext, string etag)
    {
        var matches = httpContext.Request.Headers.TryGetValue("If-None-Match", out var values)
            && values.ToString()
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(value => value == "*" || string.Equals(value, etag, StringComparison.Ordinal));

        httpContext.Response.Headers[CacheStatusHeader] = matches ? "revalidated" : "miss";
        return matches;
    }

    public static string JobReportEtag(JobReportSummaryResponse report)
    {
        var builder = new StringBuilder()
            .Append("job:")
            .Append(report.OrganizationId.ToString("N"))
            .Append(':')
            .Append(report.Id.ToString("N"))
            .Append(':')
            .Append(report.UpdatedAt.UtcTicks);

        foreach (var link in report.Links.OrderBy(link => link.Id))
        {
            builder
                .Append("|link:")
                .Append(link.Id.ToString("N"))
                .Append(':')
                .Append(link.LinkedReportId.ToString("N"))
                .Append(':')
                .Append(link.LinkedStatus);
        }

        foreach (var user in report.AssignedUsers.OrderBy(user => user.Id))
        {
            builder
                .Append("|user:")
                .Append(user.Id.ToString("N"));
        }

        return ToWeakEtag(builder.ToString());
    }

    public static string JobListEtag(
        JobListViewModel response,
        Guid organizationId,
        Guid? currentUserId)
    {
        // The list contains related data that can change without updating the
        // JobReports row itself: assignments, worksheet totals, installations
        // and user-specific seen/rejection flags. Hash the complete mapped HTTP
        // representation so a 304 can never hide one of those changes.
        var representation = JsonSerializer.Serialize(response);
        var userKey = currentUserId?.ToString("N") ?? "anon";
        return ToWeakEtag($"jobs:list:{organizationId:N}:{userKey}:{representation}");
    }

    public static string JobAssignedEtag(
        IReadOnlyList<JobListItemViewModel> jobs,
        Guid organizationId,
        Guid? currentUserId)
    {
        var representation = JsonSerializer.Serialize(jobs);
        var userKey = currentUserId?.ToString("N") ?? "anon";
        return ToWeakEtag($"jobs:assigned:{organizationId:N}:{userKey}:{representation}");
    }

    public static string JobHistoryEtag(Guid jobId, IEnumerable<JobHistoryResponse> events, int? limit, int? offset)
    {
        var builder = new StringBuilder()
            .Append("jobs:history:")
            .Append(jobId.ToString("N"))
            .Append(':')
            .Append(limit?.ToString() ?? "default")
            .Append(':')
            .Append(offset?.ToString() ?? "default");

        foreach (var evt in events.OrderBy(e => e.Id))
        {
            builder
                .Append('|')
                .Append(evt.Id.ToString("N"))
                .Append(':')
                .Append(evt.CreatedAt.ToUnixTimeMilliseconds())
                .Append(':')
                .Append(evt.EventType);
        }

        return ToWeakEtag(builder.ToString());
    }

    public static string ReferenceDataEtag(ReferenceDataResponse data)
    {
        var sb = new StringBuilder("reference-data:");
        foreach (var type in data.InstallationTypes)
        {
            sb.Append(type.Id).Append(type.SortOrder);
            foreach (var cat in type.Categories)
            {
                sb.Append(cat.Id).Append(cat.SortOrder);
                foreach (var cp in cat.ControlPoints)
                    sb.Append(cp.Id).Append(cp.SortOrder).Append(cp.IsRequired);
            }
        }
        foreach (var wk in data.WorkKinds)
            sb.Append(wk.NormalizedLabel).Append(wk.SortOrder).Append(wk.RequiresCustomWorkKind);
        foreach (var cf in data.ClosureFlags)
            sb.Append(cf.Id).Append(cf.SortOrder).Append(cf.IsExclusive);
        return ToWeakEtag(sb.ToString());
    }

    private static string ToWeakEtag(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"W/\"{Convert.ToHexString(hash)[..24].ToLowerInvariant()}\"";
    }
}
