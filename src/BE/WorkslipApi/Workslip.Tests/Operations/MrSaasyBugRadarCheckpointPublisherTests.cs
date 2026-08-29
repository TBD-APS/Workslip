using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Workslip.Application.Diagnostics;
using Workslip.Infrastructure.Operations;

namespace Workslip.Tests.Operations;

public sealed class MrSaasyBugRadarCheckpointPublisherTests
{
    [Fact]
    public async Task PublishAsync_posts_an_idempotent_sanitized_checkpoint_with_credential_headers()
    {
        var handler = new CapturingHandler(HttpStatusCode.Created);
        using var client = new HttpClient(handler);
        var publisher = new MrSaasyBugRadarCheckpointPublisher(
            client,
            Options.Create(new MrSaasyBugRadarOptions
            {
                BaseUrl = "https://app.mrsoftware.dk/",
                ActivityToken = "activity-token",
                CloudflareAccessClientId = "worker.access",
                CloudflareAccessClientSecret = "cloudflare-secret",
                AgentId = "workslip-bug-radar",
                Environment = "production"
            }));
        var lastSeen = new DateTimeOffset(2026, 8, 29, 10, 15, 30, TimeSpan.Zero);

        var published = await publisher.PublishAsync(Dashboard(new ErrorDiagnosticsItem(
            lastSeen,
            lastSeen.AddMinutes(-4),
            lastSeen,
            "backend",
            "error",
            "InvalidOperationException",
            "abc123def456",
            "Sanitized message",
            "/api/jobs/:id",
            "DELETE /api/jobs/:id",
            "release-1",
            "correlation",
            "trace",
            1,
            1,
            1,
            7)));

        Assert.Equal(1, published);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://app.mrsoftware.dk/api/activity/checkpoints", request.Url);
        Assert.Equal("activity-token", request.Headers["X-MR-SAASY-ACTIVITY-TOKEN"]);
        Assert.Equal("worker.access", request.Headers["CF-Access-Client-Id"]);
        Assert.Equal("cloudflare-secret", request.Headers["CF-Access-Client-Secret"]);
        Assert.DoesNotContain("activity-token", request.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("cloudflare-secret", request.Body, StringComparison.Ordinal);

        using var body = JsonDocument.Parse(request.Body);
        Assert.Equal("workslip-bug-abc123def456-1787998530", body.RootElement.GetProperty("id").GetString());
        Assert.Equal("Failed", body.RootElement.GetProperty("state").GetString());
        Assert.Equal("Checkpoint", body.RootElement.GetProperty("kind").GetString());
        Assert.Equal("workslip:bug:abc123def456", body.RootElement.GetProperty("correlationId").GetString());
        Assert.Equal("https://app.mrsoftware.dk/superadmin", body.RootElement.GetProperty("evidenceReference").GetString());
        Assert.Equal("InvalidOperationException: Sanitized message", body.RootElement.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task PublishAsync_skips_incomplete_or_stale_diagnostics_to_avoid_misrepresenting_the_radar()
    {
        var handler = new CapturingHandler(HttpStatusCode.Created);
        using var client = new HttpClient(handler);
        var publisher = new MrSaasyBugRadarCheckpointPublisher(
            client,
            Options.Create(new MrSaasyBugRadarOptions
            {
                BaseUrl = "https://app.mrsoftware.dk/",
                ActivityToken = "activity-token"
            }));
        var dashboard = Dashboard(SampleItem()) with { IsComplete = false, IsStale = true };

        var published = await publisher.PublishAsync(dashboard);

        Assert.Equal(0, published);
        Assert.Empty(handler.Requests);
    }

    private static ErrorDiagnosticsDashboard Dashboard(ErrorDiagnosticsItem item) => new(
        true,
        true,
        false,
        null,
        new DateTimeOffset(2026, 8, 29, 10, 20, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 29, 10, 20, 0, TimeSpan.Zero),
        true,
        true,
        true,
        false,
        false,
        new ErrorDiagnosticsSummary(1, 1, 1, 0, 1),
        null,
        [item]);

    private static ErrorDiagnosticsItem SampleItem() => new(
        new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 29, 9, 59, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 29, 10, 0, 0, TimeSpan.Zero),
        "backend",
        "error",
        "Error",
        "abc123def456",
        "Sanitized message",
        null,
        null,
        null,
        null,
        null,
        0,
        0,
        0,
        1);

    private sealed class CapturingHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(
                pair => pair.Key,
                pair => string.Join(",", pair.Value),
                StringComparer.OrdinalIgnoreCase);
            Requests.Add(new CapturedRequest(
                request.RequestUri?.ToString() ?? string.Empty,
                headers,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));

            return new HttpResponseMessage(statusCode);
        }
    }

    private sealed record CapturedRequest(
        string Url,
        IReadOnlyDictionary<string, string> Headers,
        string Body);
}
