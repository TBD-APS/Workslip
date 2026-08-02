using System.Net;
using System.Text;
using Ardalis.Result;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Diagnostics;
using Workslip.Infrastructure.Diagnostics;

namespace Workslip.Tests.Diagnostics;

public sealed class ApplicationInsightsErrorDiagnosticsServiceTests
{
    [Fact]
    public async Task GetAsync_SanitizesAndGroupsApplicationInsightsRows()
    {
        var handler = new QueueHttpMessageHandler(
            JsonResponse(SummaryJson),
            JsonResponse(DetailsJson));
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.loganalytics.azure.com/")
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Azure:ApplicationInsights:WorkspaceId"] = "workspace-id"
            })
            .Build();
        var service = new ApplicationInsightsErrorDiagnosticsService(
            httpClient,
            new FakeTokenCredential(),
            configuration,
            NullLogger<ApplicationInsightsErrorDiagnosticsService>.Instance);

        var result = await service.GetAsync(new ErrorDiagnosticsQuery("24h", "all", 50));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsAvailable);
        Assert.Equal(2, result.Value.Summary.Last24Hours);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(2, item.Occurrences);
        Assert.DoesNotContain("user@example.com", item.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("92779e5b-da5b-4cc4-bbeb-07b40cab806f", item.Message, StringComparison.Ordinal);
        Assert.Equal("/api/jobs/:id", item.Route);
        Assert.Equal("safe-correlation-id", item.CorrelationId);
        Assert.Matches("^[a-f0-9]{12}$", item.Fingerprint);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
            Assert.Equal("Bearer test-token", request.Authorization));
    }

    [Fact]
    public async Task GetAsync_ReturnsSafeUnavailableStateWhenWorkspaceIsNotConfigured()
    {
        using var httpClient = new HttpClient(new QueueHttpMessageHandler())
        {
            BaseAddress = new Uri("https://api.loganalytics.azure.com/")
        };
        var service = new ApplicationInsightsErrorDiagnosticsService(
            httpClient,
            new FakeTokenCredential(),
            new ConfigurationBuilder().Build(),
            NullLogger<ApplicationInsightsErrorDiagnosticsService>.Instance);

        var result = await service.GetAsync(new ErrorDiagnosticsQuery());

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsAvailable);
        Assert.Equal("not_configured", result.Value.AvailabilityReason);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task GetAsync_RejectsNonAllowlistedFiltersBeforeCallingAzure()
    {
        var handler = new QueueHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.loganalytics.azure.com/")
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Azure:ApplicationInsights:WorkspaceId"] = "workspace-id"
            })
            .Build();
        var service = new ApplicationInsightsErrorDiagnosticsService(
            httpClient,
            new FakeTokenCredential(),
            configuration,
            NullLogger<ApplicationInsightsErrorDiagnosticsService>.Instance);

        var result = await service.GetAsync(new ErrorDiagnosticsQuery("30d", "custom | take 1", 500));

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(3, result.ValidationErrors.Count());
        Assert.Empty(handler.Requests);
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private const string SummaryJson = """
        {
          "tables": [{
            "name": "PrimaryResult",
            "columns": [
              { "name": "LastHour", "type": "long" },
              { "name": "Last24Hours", "type": "long" },
              { "name": "Last7Days", "type": "long" },
              { "name": "FrontendLast24Hours", "type": "long" },
              { "name": "BackendLast24Hours", "type": "long" }
            ],
            "rows": [[1, 2, 3, 0, 2]]
          }]
        }
        """;

    private const string DetailsJson = """
        {
          "tables": [{
            "name": "PrimaryResult",
            "columns": [
              { "name": "Timestamp", "type": "datetime" },
              { "name": "Source", "type": "string" },
              { "name": "Severity", "type": "string" },
              { "name": "ErrorType", "type": "string" },
              { "name": "Message", "type": "string" },
              { "name": "Route", "type": "string" },
              { "name": "Operation", "type": "string" },
              { "name": "Release", "type": "string" },
              { "name": "CorrelationId", "type": "string" },
              { "name": "TraceId", "type": "string" }
            ],
            "rows": [
              ["2026-08-02T00:00:00Z", "backend", "error", "SqlException", "Failure for user@example.com and job 92779e5b-da5b-4cc4-bbeb-07b40cab806f", "/api/jobs/92779e5b-da5b-4cc4-bbeb-07b40cab806f", "POST /api/jobs", "release-1", "safe-correlation-id", "trace-1"],
              ["2026-08-01T23:59:00Z", "backend", "error", "SqlException", "Failure for user@example.com and job 92779e5b-da5b-4cc4-bbeb-07b40cab806f", "/api/jobs/92779e5b-da5b-4cc4-bbeb-07b40cab806f", "POST /api/jobs", "release-1", "safe-correlation-id", "trace-1"]
            ]
          }]
        }
        """;

    private sealed class FakeTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class QueueHttpMessageHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.RequestUri?.ToString() ?? string.Empty,
                request.Headers.Authorization?.ToString(),
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));

            if (_responses.Count == 0)
                throw new InvalidOperationException("No configured response.");

            return _responses.Dequeue();
        }
    }

    private sealed record CapturedRequest(string Url, string? Authorization, string? Body);
}
