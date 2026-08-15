using System.Text.RegularExpressions;

namespace Workslip.Api.Helpers;

internal sealed record PowerBiReportLinks(string ReportUrl, string EmbedUrl);

internal static partial class PowerBiReportUrl
{
    internal static PowerBiReportLinks? Parse(string? configuredUrl)
    {
        if (string.IsNullOrWhiteSpace(configuredUrl)
            || !Uri.TryCreate(configuredUrl.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.Equals(uri.Host, "app.powerbi.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // WOR-451 stores the normal authenticated Power BI Service report URL.
        // Only that shape is accepted here. In particular, /view?r=... (Publish to web)
        // and arbitrary app.powerbi.com paths are intentionally rejected.
        if (segments.Length < 4
            || !string.Equals(segments[0], "groups", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[2], "reports", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(segments[3], out var reportId))
        {
            return null;
        }

        var groupSegment = segments[1];
        var isMyWorkspace = string.Equals(groupSegment, "me", StringComparison.OrdinalIgnoreCase);
        if (!isMyWorkspace && !Guid.TryParse(groupSegment, out _))
        {
            return null;
        }

        string? pageName = null;
        if (segments.Length >= 5 && PageNamePattern().IsMatch(segments[4]))
        {
            pageName = segments[4];
        }

        var reportBuilder = new UriBuilder(uri)
        {
            Query = string.Empty,
            Fragment = string.Empty,
        };

        var embedQuery = new List<string>
        {
            $"reportId={Uri.EscapeDataString(reportId.ToString("D"))}",
            "autoAuth=true",
        };

        if (!isMyWorkspace)
        {
            embedQuery.Add($"groupId={Uri.EscapeDataString(Guid.Parse(groupSegment).ToString("D"))}");
        }

        if (pageName is not null)
        {
            embedQuery.Add($"pageName={Uri.EscapeDataString(pageName)}");
        }

        var embedBuilder = new UriBuilder(Uri.UriSchemeHttps, "app.powerbi.com")
        {
            Path = "/reportEmbed",
            Query = string.Join('&', embedQuery),
        };

        return new PowerBiReportLinks(reportBuilder.Uri.AbsoluteUri, embedBuilder.Uri.AbsoluteUri);
    }

    [GeneratedRegex("^ReportSection[A-Za-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex PageNamePattern();
}
