using System.Text.Json.Nodes;

namespace Boney.Delivery;

public delegate Task<JsonNode> GitHubRequest(string path);
public sealed record ReleaseContext(string Repository, string Ref, string Sha, string Event);

public sealed class GitHubReleaseGate
{
    private readonly JsonObject profile;
    private readonly string repository;
    private readonly string branch;
    private readonly string[] events;
    private readonly long[] mergerIds;

    public GitHubReleaseGate(JsonObject policy)
    {
        profile = DeliveryProfile.Validate(policy);
        repository = profile.Text("repository"); branch = profile.Text("branch");
        events = ((JsonArray)profile["events"]!).Select(n => n!.GetValue<string>()).ToArray();
        mergerIds = ((JsonArray)profile["mergerIds"]!).Select(Json.Integer).ToArray();
    }

    private async Task CurrentAsync(GitHubRequest request, string sha)
    {
        var current = await request($"/repos/{repository}/git/ref/heads/{branch}");
        if (current["object"].Text("sha") != sha) throw Json.Error("RELEASE_SOURCE_STALE");
    }

    public async Task<JsonObject> VerifyOwnerMergeAsync(GitHubRequest request, string sha)
    {
        if (!Json.Sha(sha)) throw Json.Error("RELEASE_CONTEXT_INVALID");
        var associated = await request($"/repos/{repository}/commits/{sha}/pulls?per_page=100") as JsonArray;
        if (associated is not { Count: > 0 and < 100 } || associated.Any(pr => pr.Number("number") <= 0)) throw Json.Error("MERGE_EVIDENCE_INCOMPLETE");
        var matches = new List<JsonObject>();
        foreach (var number in associated.Select(pr => pr.Number("number")).Distinct())
        {
            var pr = await request($"/repos/{repository}/pulls/{number}");
            if (pr.Number("number") == number && pr.True("merged") && pr.Text("merged_at").Length > 0
                && pr.Text("merge_commit_sha") == sha && pr["base"].Text("ref") == branch
                && pr["base"]?["repo"].Text("full_name") == repository && mergerIds.Contains(pr["merged_by"].Number("id")))
                matches.Add(new JsonObject { ["pullRequest"] = number, ["mergerId"] = pr["merged_by"].Number("id") });
        }
        if (matches.Count != 1) throw Json.Error("OWNER_MERGE_REQUIRED");
        return matches[0];
    }

    public async Task<JsonObject> AuthorizeAsync(ReleaseContext context, GitHubRequest request)
    {
        if (context.Repository != repository || context.Ref != $"refs/heads/{branch}" || !Json.Sha(context.Sha) || !events.Contains(context.Event)) throw Json.Error("RELEASE_CONTEXT_INVALID");
        await CurrentAsync(request, context.Sha);
        var result = await VerifyOwnerMergeAsync(request, context.Sha);
        await CurrentAsync(request, context.Sha);
        result["repository"] = repository; result["sha"] = context.Sha;
        if (profile["ci"] is not null) result["ciRunId"] = (await ResolveEvidenceAsync(request, context.Sha)).Number("runId");
        return result;
    }

    private string Workflow => profile["ci"].Text("workflow");
    public string WorkflowRunsPath(string sha) => $"/actions/workflows/{Workflow}/runs?branch={Uri.EscapeDataString(branch)}&head_sha={sha}&per_page=20";

    private void Canonical(JsonNode? run)
    {
        if (run.Text("path") != $".github/workflows/{Workflow}" || run.Number("id") <= 0 || run.Number("run_attempt") <= 0) throw Json.Error("CI_RUN_IDENTITY_INVALID");
    }

    public (string State, JsonObject? Run) ChooseRun(JsonArray runs, string sha)
    {
        if (!Json.Sha(sha) || Workflow.Length == 0) throw Json.Error("CI_POLICY_INVALID");
        var matching = runs.OfType<JsonObject>().Where(run => run.Text("head_sha") == sha && run.Text("head_branch") == branch && events.Contains(run.Text("event")) && run.Text("path") == $".github/workflows/{Workflow}").ToList();
        foreach (var item in matching) Canonical(item);
        var latest = matching.OrderByDescending(run => run.Number("id")).ThenByDescending(run => run.Number("run_attempt")).FirstOrDefault();
        if (latest is null) return ("missing", null);
        if (latest.Text("status") == "completed" && latest.Text("conclusion") == "success") return ("success", latest);
        if (new[] { "queued", "in_progress", "waiting", "requested", "pending" }.Contains(latest.Text("status"))) return ("pending", null);
        return ("failed", latest);
    }

    public JsonObject ValidateEvidence(string sha, string mainSha, JsonObject run, JsonArray jobs)
    {
        if (!Json.Sha(sha) || mainSha != sha) throw Json.Error("RELEASE_SOURCE_STALE");
        Canonical(run);
        if (run.Text("head_sha") != sha || run.Text("head_branch") != branch || !events.Contains(run.Text("event")) || run.Text("status") != "completed" || run.Text("conclusion") != "success") throw Json.Error("CI_RUN_NOT_GREEN");
        var ids = new HashSet<long>();
        foreach (var job in jobs)
            if (job.Number("id") <= 0 || !ids.Add(job.Number("id")) || job.Number("run_id") != run.Number("id") || job.Number("run_attempt") != run.Number("run_attempt") || job.Text("head_sha") != sha) throw Json.Error("CI_JOB_IDENTITY_INVALID");
        var gates = new List<JsonNode>();
        foreach (var name in (JsonArray)profile["ci"]!["requiredJobs"]!)
        {
            var found = jobs.Where(job => job.Text("name") == name!.GetValue<string>()).ToArray();
            if (found.Length != 1 || found[0].Text("status") != "completed" || found[0].Text("conclusion") != "success") throw Json.Error("CI_REQUIRED_JOB_NOT_GREEN");
            gates.Add(found[0]!);
        }
        return new JsonObject { ["sha"] = sha, ["runId"] = run.Number("id"), ["runUrl"] = run.Text("html_url"), ["gateId"] = gates[0].Number("id"), ["gateUrl"] = gates[0].Text("html_url") };
    }

    private static async Task<JsonArray> CollectionAsync(GitHubRequest request, string path, string field, int size, bool explicitFirst = false)
    {
        var items = new JsonArray(); var ids = new HashSet<long>(); long? total = null;
        for (var page = 1; page <= (int)Math.Ceiling(1000d / size); page++)
        {
            var response = await request(page == 1 && !explicitFirst ? path : $"{path}&page={page}");
            var count = response.Number("total_count");
            if (count is < 0 or > 1000 || total is not null && total != count || response[field] is not JsonArray entries || entries.Count != Math.Min(size, count - items.Count)) throw Json.Error("CI_EVIDENCE_INCOMPLETE");
            total = count;
            foreach (var item in entries)
            {
                if (item.Number("id") <= 0 || !ids.Add(item.Number("id"))) throw Json.Error("CI_EVIDENCE_IDENTITY_INVALID");
                items.Add(item!.DeepClone());
            }
            if (items.Count == total) return items;
        }
        throw Json.Error("CI_EVIDENCE_INCOMPLETE");
    }

    public async Task<JsonObject> ResolveEvidenceAsync(GitHubRequest request, string sha, long ciRunId = 0, int waitSeconds = 0, int pollSeconds = 45, CancellationToken cancellationToken = default)
    {
        if (!Json.Sha(sha) || ciRunId < 0 || waitSeconds < 0 || pollSeconds <= 0 || Workflow.Length == 0) throw Json.Error("CI_REQUEST_INVALID");
        var deadline = DateTimeOffset.UtcNow.AddSeconds(waitSeconds);
        async Task<(string State, JsonObject? Run)> Discover() => ChooseRun(await CollectionAsync(request, $"/repos/{repository}{WorkflowRunsPath(sha)}", "workflow_runs", 20), sha);
        async Task<JsonObject> Load(long id)
        {
            var run = await request($"/repos/{repository}/actions/runs/{id}") as JsonObject ?? throw Json.Error("CI_RUN_IDENTITY_INVALID");
            Canonical(run);
            if (run.Number("id") != id) throw Json.Error("CI_RUN_IDENTITY_INVALID");
            return run;
        }
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await CurrentAsync(request, sha); await VerifyOwnerMergeAsync(request, sha);
            var discovered = await Discover();
            if (discovered.State == "success")
            {
                var selected = discovered.Run!;
                if (ciRunId != 0 && selected.Number("id") != ciRunId) throw Json.Error("CI_RUN_NOT_LATEST");
                var run = await Load(selected.Number("id"));
                if (run.Number("run_attempt") != selected.Number("run_attempt")) throw Json.Error("CI_ATTEMPT_CHANGED");
                var jobs = await CollectionAsync(request, $"/repos/{repository}/actions/runs/{run.Number("id")}/attempts/{run.Number("run_attempt")}/jobs?per_page=100", "jobs", 100, true);
                ValidateEvidence(sha, sha, run, jobs);
                var evidence = ValidateEvidence(sha, sha, await Load(run.Number("id")), jobs);
                var latest = await Discover();
                if (latest.State != "success" || latest.Run.Number("id") != run.Number("id") || latest.Run.Number("run_attempt") != run.Number("run_attempt")) throw Json.Error("CI_ATTEMPT_CHANGED");
                await CurrentAsync(request, sha);
                return evidence;
            }
            if (discovered.State == "failed") throw Json.Error("CI_RUN_NOT_GREEN");
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) throw Json.Error("CI_WAIT_EXPIRED");
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(Math.Min(pollSeconds, 60), remaining.TotalSeconds)), cancellationToken);
        }
    }
}
