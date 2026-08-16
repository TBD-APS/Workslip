using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Workslip.Application.Operations;
using Workslip.Infrastructure.Operations;
using Xunit;

namespace Workslip.Tests.Operations;

public sealed class GitHubActionsAutomationRunProviderTests
{
    [Fact]
    public async Task ListRunsAsync_maps_success_and_stale_runs_with_evidence()
    {
        var now = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.Zero);
        const string json = """
        {
          "workflow_runs": [
            {
              "id": 101,
              "name": "CI",
              "status": "completed",
              "conclusion": "success",
              "head_sha": "abc123",
              "html_url": "https://github.com/rasm105k/Workslip-v2.0/actions/runs/101",
              "created_at": "2026-08-15T19:00:00Z",
              "updated_at": "2026-08-15T19:05:00Z",
              "run_started_at": "2026-08-15T19:01:00Z",
              "run_attempt": 2,
              "pull_requests": [{ "number": 652 }]
            },
            {
              "id": 102,
              "name": "Long running",
              "status": "in_progress",
              "conclusion": null,
              "head_sha": "def456",
              "html_url": "https://github.com/rasm105k/Workslip-v2.0/actions/runs/102",
              "created_at": "2026-08-15T18:00:00Z",
              "updated_at": "2026-08-15T18:30:00Z",
              "run_started_at": "2026-08-15T18:01:00Z",
              "run_attempt": 1,
              "pull_requests": []
            }
          ]
        }
        """;
        var provider = CreateProvider(HttpStatusCode.OK, json, now, staleAfterMinutes: 45);

        var result = await provider.ListRunsAsync(CreateRegistration(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        var success = result.Value[0];
        Assert.Equal(AutomationRunState.Succeeded, success.State);
        Assert.Equal("abc123", success.Revision);
        Assert.Equal("#652", success.PullRequest);
        Assert.Equal(2, success.Attempt);
        Assert.Equal("github-actions", success.Evidence.Provider);
        Assert.Equal("101", success.Evidence.ExternalId);
        Assert.Equal(AutomationRunState.Stale, result.Value[1].State);
    }

    [Fact]
    public async Task ListRunsAsync_returns_error_when_github_is_unavailable()
    {
        var provider = CreateProvider(
            HttpStatusCode.ServiceUnavailable,
            "{}",
            DateTimeOffset.UtcNow,
            staleAfterMinutes: 45);

        var result = await provider.ListRunsAsync(CreateRegistration(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("github_actions_unavailable", result.Errors);
    }

    private static GitHubActionsAutomationRunProvider CreateProvider(
        HttpStatusCode statusCode,
        string json,
        DateTimeOffset now,
        int staleAfterMinutes)
    {
        var client = new HttpClient(new StubHandler(statusCode, json))
        {
            BaseAddress = new Uri("https://api.github.com/")
        };
        var options = Options.Create(new GitHubActionsControlCenterOptions
        {
            RunLimit = 50,
            StaleAfterMinutes = staleAfterMinutes
        });
        return new GitHubActionsAutomationRunProvider(client, options, new FixedTimeProvider(now));
    }

    private static ApplicationEnvironmentRegistration CreateRegistration() =>
        new(
            new ApplicationEnvironmentKey("workslip", "production"),
            "Workslip",
            [
                new ControlCenterSourceRegistration(
                    ControlCenterSignalKind.Automation,
                    new EvidenceReference("github-actions", "rasm105k/Workslip-v2.0"))
            ]);

    private sealed class StubHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(
                "https://api.github.com/repos/rasm105k/Workslip-v2.0/actions/runs?per_page=50",
                request.RequestUri?.ToString());

            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
