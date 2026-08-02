using System.Net;
using System.Text;
using Ardalis.Result;
using Azure.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Diagnostics;
using Workslip.Infrastructure.Diagnostics;

namespace Workslip.Tests.Diagnostics;

public sealed class ApplicationInsightsErrorDiagnosticsServiceTests
{
    [Fact]
    public async Task GetAsync_UsesWeightedCountsAndGroupsSanitizedRows()
    {
        var handler = new QueryHttpMessageHandler(request =>
            JsonResponse(request.Body.Contains("LastHour", StringComparison.Ordinal) ? SummaryJson : DetailsJson));
        using var serviceFixture = CreateService(handler);

        var result = await serviceFixture.Service.GetAsync(new ErrorDiagnosticsQuery("24h", "all", 50));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsAvailable);
        Assert.True(result.Value.IsComplete);
        Assert.False(result.Value.IsStale);
        Assert.True(result.Value.SummaryAvailable);
        Assert.True(result.Value.ItemsAvailable);
        Assert.Equal(7, result.Value.Summary!.Last24Hours);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(10, item.Occurrences);
        Assert.DoesNotContain("user@example.com", item.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("92779e5b-da5b-4cc4-bbeb-07b40cab806f", item.Message, StringComparison.Ordinal);
        Assert.Equal("/api/jobs/:id", item.Route);
        Assert.Equal("safe-correlation-id", item.CorrelationId);
        Assert.Matches("^[a-f0-9]{12}$", item.Fingerprint);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
            Assert.Equal("Bearer test-token", request.Authorization));
        Assert.Contains(handler.Requests, request => request.Body.Contains("sumif(Weight", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, request => request.Body.Contains("Occurrences = sum(Weight)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetAsync_DoesNotTurnMalformedSummaryIntoZeroErrors()
    {
        var handler = new QueryHttpMessageHandler(request =>
            JsonResponse(request.Body.Contains("LastHour", StringComparison.Ordinal) ? MalformedSummaryJson : DetailsJson));
        using var serviceFixture = CreateService(handler);

        var result = await serviceFixture.Service.GetAsync(new ErrorDiagnosticsQuery());

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsAvailable);
        Assert.False(result.Value.IsComplete);
        Assert.False(result.Value.SummaryAvailable);
        Assert.True(result.Value.ItemsAvailable);
        Assert.Null(result.Value.Summary);
        Assert.Equal("invalid_response", result.Value.AvailabilityReason);
        Assert.NotEmpty(result.Value.Items);
    }

    [Fact]
    public async Task GetAsync_MarksAzurePartialErrorAsIncomplete()
    {
        var handler = new QueryHttpMessageHandler(request =>
            JsonResponse(request.Body.Contains("LastHour", StringComparison.Ordinal) ? SummaryJson : PartialDetailsJson));
        using var serviceFixture = CreateService(handler);

        var result = await serviceFixture.Service.GetAsync(new ErrorDiagnosticsQuery());

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsAvailable);
        Assert.False(result.Value.IsComplete);
        Assert.True(result.Value.HasPartialAzureResults);
        Assert.Equal("partial_result", result.Value.AvailabilityReason);
        Assert.NotEmpty(result.Value.Items);
    }

    [Fact]
    public async Task GetAsync_ReturnsLastKnownGoodSnapshotWhenAzureFails()
    {
        var fail = false;
        var handler = new QueryHttpMessageHandler(request =>
            fail
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : JsonResponse(request.Body.Contains("LastHour", StringComparison.Ordinal) ? SummaryJson : DetailsJson));
        using var serviceFixture = CreateService(handler);

        var initial = await serviceFixture.Service.GetAsync(new ErrorDiagnosticsQuery());
        fail = true;
        var fallback = await serviceFixture.Service.GetAsync(new ErrorDiagnosticsQuery());

        Assert.True(initial.Value.IsComplete);
        Assert.True(fallback.Value.IsAvailable);
        Assert.True(fallback.Value.IsStale);
        Assert.False(fallback.Value.IsComplete);
        Assert.NotNull(fallback.Value.DataRetrievedAtUtc);
        Assert.Equal(initial.Value.DataRetrievedAtUtc, fallback.Value.DataRetrievedAtUtc);
        Assert.Equal(initial.Value.Summary, fallback.Value.Summary);
        Assert.Equal(initial.Value.Items, fallback.Value.Items);
        Assert.Equal("query_failed", fallback.Value.AvailabilityReason);
    }

    [Fact]
    public async Task GetAsync_ReturnsSafeUnavailableStateWhenWorkspaceIsNotConfigured()
    {
        var handler = new QueryHttpMessageHandler(_ => throw new InvalidOperationException("Azure must not be called."));
        using var serviceFixture = CreateService(handler, configureWorkspace: false);

        var result = await serviceFixture.Service.GetAsync(new ErrorDiagnosticsQuery());

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsAvailable);
        Assert.Equal("not_configured", result.Value.AvailabilityReason);
        Assert.False(result.Value.SummaryAvailable);
        Assert.False(result.Value.ItemsAvailable);
        Assert.Null(result.Value.Summary);
        Assert.Empty(result.Value.Items);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task GetAsync_RejectsNonAllowlistedFiltersBeforeCallingAzure()
    {
        var handler = new QueryHttpMessageHandler(_ => throw new InvalidOperationException("Azure must not be called."));
        using var serviceFixture = CreateService(handler);

        var result = await serviceFixture.Service.GetAsync(new ErrorDiagnosticsQuery("30d", "custom | take 1", 500));

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(3, result.ValidationErrors.Count());
        Assert.Empty(handler.Requests);
    }

    private static ServiceFixture CreateService(
        QueryHttpMessageHandler handler,
        bool configureWorkspace = true)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.loganalytics.azure.com/")
        };
        var values = configureWorkspace
            ? new Dictionary<string, string?>
            {
                ["Azure:ApplicationInsights:WorkspaceId"] = "workspace-id"
            }
            : new Dictionary<string, string?>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new ApplicationInsightsErrorDiagnosticsService(
            httpClient,
            new FakeTokenCredential(),
            configuration,
            cache,
            NullLogger<ApplicationInsightsErrorDiagnosticsService>.Instance);

        return new ServiceFixture(service, httpClient, cache);
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
            "rows": [[3, 7, 11, 2, 5]]
          }]
        }
        """;

    private const string MalformedSummaryJson = """
        {
          "tables": [{
            "name": "PrimaryResult",
            "columns": [{ "name": "Unexpected", "type": "long" }],
            "rows": [[0]]
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
              { "name": "TraceId", "type": "string" },
              { "name": "Occurrences", "type": "long" }
            ],
            "rows": [
              ["2026-08-02T00:00:00Z", "backend", "error", "SqlException", "Failure for user@example.com and job 92779e5b-da5b-4cc4-bbeb-07b40cab806f", "/api/jobs/92779e5b-da5b-4cc4-bbeb-07b40cab806f", "POST /api/jobs", "release-1", "safe-correlation-id", "trace-1", 4],
              ["2026-08-01T23:59:00Z", "backend", "error", "SqlException", "Failure for user@example.com and job 92779e5b-da5b-4cc4-bbeb-07b40cab806f", "/api/jobs/92779e5b-da5b-4cc4-bbeb-07b40cab806f", "POST /api/jobs", "release-1", "safe-correlation-id", "trace-1", 6]
            ]
          }]
        }
        """;

    private const string PartialDetailsJson = """
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
              { "name": "TraceId", "type": "string" },
              { "name": "Occurrences", "type": "long" }
            ],
            "rows": [
              ["2026-08-02T00:00:00Z", "frontend", "error", "TypeError", "Frontend error", "/app/jobs/:id", "", "release-1", "", "trace-1", 2]
            ]
          }],
          "error": {
            "code": "PartialError",
            "message": "Partial query result",
            "details": []
          }
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

    private sealed class QueryHttpMessageHandler(
        Func<CapturedRequest, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        private readonly object _sync = new();

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var captured = new CapturedRequest(
                request.RequestUri?.ToString() ?? string.Empty,
                request.Headers.Authorization?.ToString(),
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            lock (_sync)
                Requests.Add(captured);

            return responseFactory(captured);
        }
    }

    private sealed record CapturedRequest(string Url, string? Authorization, string Body);

    private sealed class ServiceFixture(
        ApplicationInsightsErrorDiagnosticsService service,
        HttpClient httpClient,
        MemoryCache cache) : IDisposable
    {
        public ApplicationInsightsErrorDiagnosticsService Service { get; } = service;

        public void Dispose()
        {
            httpClient.Dispose();
            cache.Dispose();
        }
    }
}
