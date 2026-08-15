using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Ardalis.Result;
using Microsoft.Extensions.Options;
using Workslip.Application.Operations;

namespace Workslip.Infrastructure.Operations;

public sealed class GitHubActionsControlCenterOptions
{
    public const string SectionName = "ControlCenter:GitHubActions";

    public string? Token { get; set; }
    public int RunLimit { get; set; } = 50;
    public int StaleAfterMinutes { get; set; } = 45;
}

public sealed class GitHubActionsAutomationRunProvider(
    HttpClient httpClient,
    IOptions<GitHubActionsControlCenterOptions> options,
    TimeProvider timeProvider) : IAutomationRunProvider
{
    public string Provider => "github-actions";

    public async Task<Result<IReadOnlyList<AutomationRunSummary>>> ListRunsAsync(
        ApplicationEnvironmentRegistration application,
        CancellationToken cancellationToken)
    {
        var source = application.Sources.FirstOrDefault(item =>
            item.Kind == ControlCenterSignalKind.Automation
            && string.Equals(item.Evidence.Provider, Provider, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return Result<IReadOnlyList<AutomationRunSummary>>.Success([]);
        }

        var settings = options.Value;
        var runLimit = Math.Clamp(settings.RunLimit, 1, 100);
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"repos/{source.Evidence.Reference}/actions/runs?per_page={runLimit}");

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrWhiteSpace(settings.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Result<IReadOnlyList<AutomationRunSummary>>.Error("github_actions_unavailable");
        }

        var payload = await response.Content.ReadFromJsonAsync<GitHubRunsResponse>(cancellationToken);
        if (payload?.WorkflowRuns is null)
        {
            return Result<IReadOnlyList<AutomationRunSummary>>.Error("github_actions_invalid_response");
        }

        var now = timeProvider.GetUtcNow();
        var staleAfter = TimeSpan.FromMinutes(Math.Clamp(settings.StaleAfterMinutes, 5, 24 * 60));
        var runs = payload.WorkflowRuns
            .Select(run => Map(run, now, staleAfter))
            .ToArray();

        return Result<IReadOnlyList<AutomationRunSummary>>.Success(runs);
    }

    private static AutomationRunSummary Map(
        GitHubWorkflowRun run,
        DateTimeOffset now,
        TimeSpan staleAfter)
    {
        var startedAt = run.RunStartedAt ?? run.CreatedAt;
        var state = MapState(run.Status, run.Conclusion);
        if (state == AutomationRunState.Running && now - run.UpdatedAt > staleAfter)
        {
            state = AutomationRunState.Stale;
        }

        return new AutomationRunSummary(
            run.Id.ToString(),
            string.IsNullOrWhiteSpace(run.Name) ? "Unknown workflow" : run.Name,
            state,
            startedAt,
            string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase)
                ? run.UpdatedAt
                : null,
            run.UpdatedAt,
            Math.Max(run.RunAttempt, 1),
            run.HeadSha,
            run.PullRequests?.FirstOrDefault() is { } pullRequest ? $"#{pullRequest.Number}" : null,
            Issue: null,
            new EvidenceReference("github-actions", run.HtmlUrl, run.Id.ToString()));
    }

    internal static AutomationRunState MapState(string? status, string? conclusion)
    {
        if (!string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return status?.ToLowerInvariant() switch
            {
                "queued" or "in_progress" or "pending" or "requested" or "waiting" => AutomationRunState.Running,
                _ => AutomationRunState.Unknown
            };
        }

        return conclusion?.ToLowerInvariant() switch
        {
            "success" => AutomationRunState.Succeeded,
            "failure" or "timed_out" or "startup_failure" => AutomationRunState.Failed,
            "cancelled" => AutomationRunState.Cancelled,
            "action_required" => AutomationRunState.Blocked,
            "stale" => AutomationRunState.Stale,
            _ => AutomationRunState.Unknown
        };
    }

    private sealed class GitHubRunsResponse
    {
        [JsonPropertyName("workflow_runs")]
        public IReadOnlyList<GitHubWorkflowRun>? WorkflowRuns { get; init; }
    }

    private sealed class GitHubWorkflowRun
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("conclusion")]
        public string? Conclusion { get; init; }

        [JsonPropertyName("head_sha")]
        public string? HeadSha { get; init; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = string.Empty;

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; init; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; init; }

        [JsonPropertyName("run_started_at")]
        public DateTimeOffset? RunStartedAt { get; init; }

        [JsonPropertyName("run_attempt")]
        public int RunAttempt { get; init; }

        [JsonPropertyName("pull_requests")]
        public IReadOnlyList<GitHubPullRequestRef>? PullRequests { get; init; }
    }

    private sealed class GitHubPullRequestRef
    {
        [JsonPropertyName("number")]
        public int Number { get; init; }
    }
}
