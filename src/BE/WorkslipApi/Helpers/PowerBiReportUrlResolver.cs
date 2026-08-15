using Microsoft.AspNetCore.WebUtilities;

namespace Workslip.Api.Helpers;

public sealed record PowerBiReportUrls(string Url, string EmbedUrl);

public static class PowerBiReportUrlResolver
{
    private const string PowerBiHost = "app.powerbi.com";
    private const string SecureEmbedBaseUrl = "https://app.powerbi.com/reportEmbed";
    private const string ReportPagePrefix = "ReportSection";

    public static PowerBiReportUrls? Resolve(string? configuredUrl)
    {
        if (string.IsNullOrWhiteSpace(configuredUrl)
            || !Uri.TryCreate(configuredUrl.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.Equals(uri.Host, PowerBiHost, StringComparison.OrdinalIgnoreCase)
            || !TryGetReportCoordinates(
                uri,
                out var reportId,
                out var groupId,
                out var tenantId,
                out var pageName))
        {
            return null;
        }

        var parameters = new Dictionary<string, string?>
        {
            ["reportId"] = reportId.ToString("D"),
            ["autoAuth"] = "true",
        };

        if (groupId.HasValue)
        {
            parameters["groupId"] = groupId.Value.ToString("D");
        }

        if (tenantId.HasValue)
        {
            parameters["ctid"] = tenantId.Value.ToString("D");
        }

        if (pageName is not null)
        {
            parameters["pageName"] = pageName;
        }

        var fallbackBuilder = new UriBuilder(uri)
        {
            Fragment = string.Empty,
        };

        return new PowerBiReportUrls(
            fallbackBuilder.Uri.AbsoluteUri,
            QueryHelpers.AddQueryString(SecureEmbedBaseUrl, parameters));
    }

    private static bool TryGetReportCoordinates(
        Uri uri,
        out Guid reportId,
        out Guid? groupId,
        out Guid? tenantId,
        out string? pageName)
    {
        reportId = Guid.Empty;
        groupId = null;
        tenantId = null;
        pageName = null;

        if (!TryGetOptionalGuidQueryValue(uri, "ctid", out tenantId))
        {
            return false;
        }

        if (string.Equals(uri.AbsolutePath.TrimEnd('/'), "/reportEmbed", StringComparison.OrdinalIgnoreCase))
        {
            var query = QueryHelpers.ParseQuery(uri.Query);
            if (!Guid.TryParse(query["reportId"].FirstOrDefault(), out reportId)
                || !TryGetOptionalGuidQueryValue(uri, "groupId", out groupId))
            {
                return false;
            }

            var queryPageName = query["pageName"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(queryPageName))
            {
                if (!IsSafePageName(queryPageName))
                {
                    return false;
                }

                pageName = queryPageName;
            }

            return true;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length < 4
            || !string.Equals(segments[0], "groups", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(segments[2], "reports", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(segments[3], out reportId))
        {
            return false;
        }

        if (!string.Equals(segments[1], "me", StringComparison.OrdinalIgnoreCase))
        {
            if (!Guid.TryParse(segments[1], out var parsedGroupId))
            {
                return false;
            }

            groupId = parsedGroupId;
        }

        if (segments.Length >= 5 && IsSafePageName(segments[4]))
        {
            pageName = segments[4];
        }

        return true;
    }

    private static bool TryGetOptionalGuidQueryValue(Uri uri, string key, out Guid? value)
    {
        value = null;
        var raw = QueryHelpers.ParseQuery(uri.Query)[key].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (!Guid.TryParse(raw, out var parsed))
        {
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool IsSafePageName(string value)
    {
        if (!value.StartsWith(ReportPagePrefix, StringComparison.Ordinal)
            || value.Length > 128)
        {
            return false;
        }

        return value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '_' or '-');
    }
}
