using Microsoft.AspNetCore.WebUtilities;

namespace Workslip.Api.Helpers;

public sealed record PowerBiReportUrls(string Url, string EmbedUrl);

public static class PowerBiReportUrlResolver
{
    private const string PowerBiHost = "app.powerbi.com";
    private const string SecureEmbedBaseUrl = "https://app.powerbi.com/reportEmbed";

    public static PowerBiReportUrls? Resolve(string? configuredUrl)
    {
        if (string.IsNullOrWhiteSpace(configuredUrl)
            || !Uri.TryCreate(configuredUrl.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, PowerBiHost, StringComparison.OrdinalIgnoreCase)
            || !TryGetReportCoordinates(uri, out var reportId, out var groupId, out var tenantId))
        {
            return null;
        }

        var parameters = new Dictionary<string, string?>
        {
            ["reportId"] = reportId.ToString(),
            ["autoAuth"] = "true",
        };

        if (groupId.HasValue)
        {
            parameters["groupId"] = groupId.Value.ToString();
        }

        if (tenantId.HasValue)
        {
            parameters["ctid"] = tenantId.Value.ToString();
        }

        return new PowerBiReportUrls(
            uri.AbsoluteUri,
            QueryHelpers.AddQueryString(SecureEmbedBaseUrl, parameters));
    }

    private static bool TryGetReportCoordinates(
        Uri uri,
        out Guid reportId,
        out Guid? groupId,
        out Guid? tenantId)
    {
        reportId = Guid.Empty;
        groupId = null;
        tenantId = TryGetGuidQueryValue(uri, "ctid");

        if (string.Equals(uri.AbsolutePath.TrimEnd('/'), "/reportEmbed", StringComparison.OrdinalIgnoreCase))
        {
            var reportIdValue = QueryHelpers.ParseQuery(uri.Query)["reportId"].FirstOrDefault();
            if (!Guid.TryParse(reportIdValue, out reportId))
            {
                return false;
            }

            groupId = TryGetGuidQueryValue(uri, "groupId");
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

        if (string.Equals(segments[1], "me", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!Guid.TryParse(segments[1], out var parsedGroupId))
        {
            return false;
        }

        groupId = parsedGroupId;
        return true;
    }

    private static Guid? TryGetGuidQueryValue(Uri uri, string key)
    {
        var value = QueryHelpers.ParseQuery(uri.Query)[key].FirstOrDefault();
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }
}
