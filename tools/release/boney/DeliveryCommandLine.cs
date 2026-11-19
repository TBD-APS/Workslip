using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Boney.Delivery;

public static class DeliveryCommandLine
{
    public static async Task<JsonObject> RunAsync(string[] args)
    {
        if (args is ["verify-snapshot", var snapshot]) return DeliverySnapshot.Verify(snapshot);
        var root = Environment.CurrentDirectory;
        VerifyRuntimeSnapshot();
        if (args is ["export", var target, var revision]) { await DeliverySnapshot.ExportAsync(target, revision); return DeliverySnapshot.Verify(target); }
        if (args is ["validate-profile", var file]) return DeliveryProfile.Load(file);
        if (args.Length == 0 || (args.Length - 1) % 2 != 0) throw Json.Error("DELIVERY_USAGE");
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < args.Length; i += 2)
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || string.IsNullOrEmpty(args[i + 1]) || !options.TryAdd(args[i], args[i + 1])) throw Json.Error("DELIVERY_USAGE");
        string Required(string name) => options.TryGetValue(name, out var value) ? value : throw Json.Error("DELIVERY_USAGE");
        void Only(params string[] names) { if (options.Keys.Any(key => !names.Contains(key))) throw Json.Error("DELIVERY_USAGE"); }
        switch (args[0])
        {
            case "publish": Only("--out"); return await ReferenceRelease.PublishAsync(root, options.GetValueOrDefault("--out"));
            case "verify-package": Only("--package", "--revision"); return ReferenceRelease.VerifyPackage(Required("--package"), Required("--revision"));
            case "observe":
                Only("--url", "--revision", "--out"); var observation = await ReferenceRelease.ObserveAsync(Required("--url"), Required("--revision"));
                if (options.TryGetValue("--out", out var output)) ReferenceRelease.SaveReceipt(output, observation); return observation;
            case "container-proof": Only("--revision", "--image", "--package", "--temporary"); return await ReferenceRelease.ContainerProofAsync(Required("--revision"), Required("--image"), Required("--package"), Required("--temporary"));
            case "admit": case "run":
                Only("--profile", "--revision", "--adapter", "--environment", "--store", "--receipt");
                var sha = Required("--revision");
                var paths = args[0] == "run" ? new[] { Required("--profile"), Required("--adapter") } : [Required("--profile")];
                var tracked = await TrackedSources.RequireAsync(sha, paths);
                var profile = DeliveryProfile.Load(tracked[0]);
                var context = new ReleaseContext(Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") ?? "", Environment.GetEnvironmentVariable("GITHUB_REF") ?? "", sha, Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME") ?? "");
                using (var http = GitHubClient())
                {
                    Task<JsonNode> Request(string path) => RequestAsync(http, path);
                    var admission = await new GitHubReleaseGate(profile).AuthorizeAsync(context, Request);
                    if (args[0] == "admit") return admission;
                    var store = Required("--store"); if (string.IsNullOrWhiteSpace(store)) throw Json.Error("DURABLE_RELEASE_STORE_REQUIRED");
                    var environment = Required("--environment"); var receipt = Required("--receipt");
                    await TrackedSources.RequireAsync(sha, paths);
                    var result = await ReleasePipeline.ExecuteAsync(profile, environment, context, new CSharpProcessAdapter(tracked[1]), new FileReleaseStore(store), Request);
                    ReferenceRelease.SaveReceipt(receipt, result); return result;
                }
            default: throw Json.Error("DELIVERY_USAGE");
        }
    }

    internal static void VerifyRuntimeSnapshot()
    {
        var sourceDirectory = FindSourceDirectory(Environment.CurrentDirectory);
        if (File.Exists(Path.Combine(sourceDirectory, "boney.lock.json"))) DeliverySnapshot.Verify(sourceDirectory);
    }

    private static string FindSourceDirectory(string root)
    {
        if (File.Exists(Path.Combine(root, "boney.lock.json"))) return root;
        // An exported build may be invoked from any working directory, including through a symlink.
        var build = new DirectoryInfo(AppContext.BaseDirectory);
        while (build is not null) { if (File.Exists(Path.Combine(build.FullName, "Boney.Delivery.csproj"))) return build.FullName; build = build.Parent; }
        return root;
    }
    private static HttpClient GitHubClient()
    {
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        if (string.IsNullOrEmpty(token)) throw Json.Error("GITHUB_READ_TOKEN_REQUIRED");
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri("https://api.github.com"), Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json"); http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28"); http.DefaultRequestHeaders.UserAgent.ParseAdd("Boney.Delivery/1"); return http;
    }
    private static async Task<JsonNode> RequestAsync(HttpClient client, string path)
    {
        if (!path.StartsWith("/repos/", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal) || path.Any(c => c is '\r' or '\n')) throw Json.Error("GITHUB_EVIDENCE_PATH_INVALID");
        using var response = await client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) throw Json.Error($"GITHUB_EVIDENCE_HTTP_{(int)response.StatusCode}");
        await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync()) ?? throw Json.Error("GITHUB_EVIDENCE_INVALID");
    }

}
