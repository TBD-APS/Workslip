using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Workslip.Application.Diagnostics;

namespace Workslip.Infrastructure.Operations;

/// <summary>
/// Publishes the sanitized error signatures exposed by <see cref="IErrorDiagnosticsService"/>
/// as provider-neutral activity checkpoints. It never forwards raw telemetry rows or response
/// bodies, and the checkpoint identifier makes retries idempotent at the receiving boundary.
/// </summary>
public sealed class MrSaasyBugRadarCheckpointPublisher(
    HttpClient httpClient,
    IOptions<MrSaasyBugRadarOptions> options)
{
    private const string ActivityTokenHeader = "X-MR-SAASY-ACTIVITY-TOKEN";
    private const string CloudflareClientIdHeader = "CF-Access-Client-Id";
    private const string CloudflareClientSecretHeader = "CF-Access-Client-Secret";
    private readonly MrSaasyBugRadarOptions _options = options.Value;

    public static bool IsPublishable(ErrorDiagnosticsDashboard dashboard) =>
        dashboard.IsAvailable &&
        dashboard.IsComplete &&
        !dashboard.IsStale &&
        dashboard.ItemsAvailable;

    public void ValidateOptions()
    {
        if (!TryGetBaseUri(_options.BaseUrl, out _))
        {
            throw new InvalidOperationException(
                "ControlCenter:MrSaasyBugRadar:BaseUrl must be an absolute HTTPS URL when publishing is enabled.");
        }

        if (string.IsNullOrWhiteSpace(_options.AgentId) ||
            _options.AgentId.Trim().Length > 128 ||
            _options.AgentId.Trim().Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new InvalidOperationException(
                "ControlCenter:MrSaasyBugRadar:AgentId is required and must use only letters, digits, hyphens or underscores when publishing is enabled.");
        }

        if (string.IsNullOrWhiteSpace(_options.Environment))
        {
            throw new InvalidOperationException(
                "ControlCenter:MrSaasyBugRadar:Environment is required when publishing is enabled.");
        }

        if (_options.RefreshIntervalMinutes is < 5 or > 1440)
        {
            throw new InvalidOperationException(
                "ControlCenter:MrSaasyBugRadar:RefreshIntervalMinutes must be between 5 and 1440.");
        }

        if (_options.ErrorLimit is < 1 or > 100)
        {
            throw new InvalidOperationException(
                "ControlCenter:MrSaasyBugRadar:ErrorLimit must be between 1 and 100.");
        }

        var hasActivityToken = !string.IsNullOrWhiteSpace(_options.ActivityToken);
        var hasCloudflareClientId = !string.IsNullOrWhiteSpace(_options.CloudflareAccessClientId);
        var hasCloudflareClientSecret = !string.IsNullOrWhiteSpace(_options.CloudflareAccessClientSecret);
        if (!hasActivityToken && !(hasCloudflareClientId && hasCloudflareClientSecret))
        {
            throw new InvalidOperationException(
                "ControlCenter:MrSaasyBugRadar requires ActivityToken or both Cloudflare Access service-token credentials when publishing is enabled.");
        }

        if (hasCloudflareClientId != hasCloudflareClientSecret)
        {
            throw new InvalidOperationException(
                "ControlCenter:MrSaasyBugRadar requires both Cloudflare Access service-token credentials when either one is configured.");
        }
    }

    public async Task<int> PublishAsync(
        ErrorDiagnosticsDashboard dashboard,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dashboard);

        if (!IsPublishable(dashboard)) return 0;

        var checkpointUri = GetCheckpointUri();
        var published = 0;
        foreach (var item in dashboard.Items)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, checkpointUri)
            {
                Content = JsonContent.Create(CreateCheckpoint(item, checkpointUri))
            };
            AddAuthenticationHeaders(request);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"MR SAAS'y activity checkpoint ingestion returned HTTP {(int)response.StatusCode}.",
                    null,
                    response.StatusCode);
            }

            published++;
        }

        return published;
    }

    private MrSaasyActivityCheckpoint CreateCheckpoint(ErrorDiagnosticsItem item, Uri checkpointUri)
    {
        var errorType = string.IsNullOrWhiteSpace(item.ErrorType) ? "Error" : item.ErrorType.Trim();
        var message = item.Message?.Trim();
        var summary = string.IsNullOrWhiteSpace(message) ? errorType : $"{errorType}: {message}";
        var route = string.IsNullOrWhiteSpace(item.Route) ? "not reported" : item.Route.Trim();
        var operation = string.IsNullOrWhiteSpace(item.Operation) ? "not reported" : item.Operation.Trim();
        if (string.IsNullOrWhiteSpace(item.Fingerprint))
        {
            throw new InvalidOperationException("MR SAAS'y Bug Radar requires a sanitized error fingerprint.");
        }

        var fingerprint = item.Fingerprint.Trim();

        var checkpointId = $"workslip-bug-{fingerprint}-{item.LastSeenUtc.ToUnixTimeSeconds()}";
        if (checkpointId.Length > 128)
        {
            throw new InvalidOperationException("MR SAAS'y Bug Radar checkpoint identifier exceeds the receiving contract limit.");
        }

        return new MrSaasyActivityCheckpoint(
            checkpointId,
            "Failed",
            "Checkpoint",
            summary,
            item.LastSeenUtc,
            _options.AgentId.Trim(),
            "workslip",
            "workslip",
            "Workslip-v2.0",
            _options.Environment.Trim(),
            "application-insights",
            $"Sanitized {item.Source} {item.Severity} exception; route: {route}; operation: {operation}.",
            "Triage the sanitized Workslip exception and append recovery evidence before closing it.",
            $"Occurrences: {item.Occurrences}; affected releases: {item.AffectedReleaseCount}; affected routes: {item.AffectedRouteCount}; affected operations: {item.AffectedOperationCount}.",
            new Uri(checkpointUri, "/superadmin").AbsoluteUri,
            $"workslip:bug:{fingerprint}");
    }

    private Uri GetCheckpointUri()
    {
        if (!TryGetBaseUri(_options.BaseUrl, out var baseUri))
        {
            throw new InvalidOperationException(
                "ControlCenter:MrSaasyBugRadar:BaseUrl must be an absolute HTTPS URL when publishing is enabled.");
        }

        return new Uri(baseUri, "api/activity/checkpoints");
    }

    private void AddAuthenticationHeaders(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(_options.ActivityToken))
        {
            request.Headers.TryAddWithoutValidation(ActivityTokenHeader, _options.ActivityToken.Trim());
        }

        if (!string.IsNullOrWhiteSpace(_options.CloudflareAccessClientId))
        {
            request.Headers.TryAddWithoutValidation(CloudflareClientIdHeader, _options.CloudflareAccessClientId.Trim());
            request.Headers.TryAddWithoutValidation(CloudflareClientSecretHeader, _options.CloudflareAccessClientSecret.Trim());
        }
    }

    private static bool TryGetBaseUri(string value, out Uri baseUri)
    {
        if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var candidate) &&
            string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            baseUri = new Uri(candidate.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
                ? candidate.AbsoluteUri
                : $"{candidate.AbsoluteUri}/");
            return true;
        }

        baseUri = null!;
        return false;
    }

    private sealed record MrSaasyActivityCheckpoint(
        string Id,
        string State,
        string Kind,
        string Summary,
        DateTimeOffset OccurredAt,
        string AgentId,
        string Provider,
        string Application,
        string Project,
        string Environment,
        string Tool,
        string Reason,
        string NextAction,
        string Impact,
        string EvidenceReference,
        string CorrelationId);
}
