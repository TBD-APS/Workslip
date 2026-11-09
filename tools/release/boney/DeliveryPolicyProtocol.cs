using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Boney.Delivery;

/// <summary>A single-operation JSON-lines bridge to the typed delivery policies.</summary>
public static class DeliveryPolicyProtocol
{
    public const int MaximumLineBytes = 4 * 1024 * 1024;
    private static readonly HashSet<string> SafeErrors = new(StringComparer.Ordinal)
    {
        "DELIVERY_PROTOCOL_INVALID", "DELIVERY_PROTOCOL_CLOSED", "DELIVERY_PROTOCOL_TOO_LARGE", "DELIVERY_PROTOCOL_OUT_OF_ORDER",
        "GITHUB_EVIDENCE_UNAVAILABLE", "GITHUB_EVIDENCE_PATH_INVALID", "DELIVERY_SNAPSHOT_INVALID", "DELIVERY_PROFILE_INVALID", "DELIVERY_SCAFFOLD_INVALID",
        "RELEASE_SOURCE_STALE", "RELEASE_CONTEXT_INVALID", "MERGE_EVIDENCE_INCOMPLETE", "OWNER_MERGE_REQUIRED",
        "CI_RUN_IDENTITY_INVALID", "CI_POLICY_INVALID", "CI_RUN_NOT_GREEN", "CI_JOB_IDENTITY_INVALID", "CI_REQUIRED_JOB_NOT_GREEN",
        "CI_EVIDENCE_INCOMPLETE", "CI_EVIDENCE_IDENTITY_INVALID", "CI_REQUEST_INVALID", "CI_RUN_NOT_LATEST", "CI_ATTEMPT_CHANGED", "CI_WAIT_EXPIRED",
        "RISK_TIER_INVALID", "RISK_GLOB_INVALID", "RISK_POLICY_REQUIRED", "RISK_POLICY_INVALID", "RISK_PATH_INVALID", "RISK_ENVIRONMENT_INVALID", "RISK_SELF_HEAL_REFUSED"
    };

    public static async Task<int> RunAsync(TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        var channel = new Channel(input, output, cancellationToken);
        try
        {
            DeliveryCommandLine.VerifyRuntimeSnapshot();
            var request = await channel.ReadAsync();
            var result = await DispatchAsync(request, channel.RequestAsync, cancellationToken);
            await channel.WriteAsync(new JsonObject { ["result"] = result });
            return 0;
        }
        catch (Exception error)
        {
            var code = error is InvalidOperationException && SafeErrors.Contains(error.Message) ? error.Message
                : error is DecoderFallbackException ? "DELIVERY_PROTOCOL_INVALID"
                : error is IOException or OperationCanceledException ? "DELIVERY_PROTOCOL_CLOSED" : "DELIVERY_POLICY_FAILED";
            try { await channel.WriteAsync(new JsonObject { ["error"] = code }); } catch { /* A closed output cannot receive an error. */ }
            return 1;
        }
    }

    public static async Task<JsonNode> DispatchAsync(JsonObject request, GitHubRequest github, CancellationToken cancellationToken = default)
    {
        switch (Text(request, "operation"))
        {
            case "validate-profile":
                Only(request, "profile"); return DeliveryProfile.Validate(Object(request, "profile"));
            case "authorize":
            {
                Only(request, "profile", "context"); var context = Object(request, "context"); Keys(context, "repository", "ref", "sha", "event");
                return await new GitHubReleaseGate(Object(request, "profile")).AuthorizeAsync(new ReleaseContext(Text(context, "repository"), Text(context, "ref"), Text(context, "sha"), Text(context, "event")), github);
            }
            case "verify-owner-merge":
                Only(request, "profile", "sha"); return await new GitHubReleaseGate(Object(request, "profile")).VerifyOwnerMergeAsync(github, Text(request, "sha"));
            case "workflow-runs-path":
            {
                Only(request, "profile", "sha"); var gate = new GitHubReleaseGate(Object(request, "profile")); var sha = Text(request, "sha");
                gate.ChooseRun(new JsonArray(), sha); // Reuse the typed SHA/CI policy validation before formatting a path.
                return JsonValue.Create(gate.WorkflowRunsPath(sha));
            }
            case "choose-run":
            {
                Only(request, "profile", "sha", "runs"); var choice = new GitHubReleaseGate(Object(request, "profile")).ChooseRun(Array(request, "runs"), Text(request, "sha"));
                return new JsonObject { ["state"] = choice.State, ["run"] = choice.Run?.DeepClone() };
            }
            case "validate-evidence":
                Only(request, "profile", "sha", "mainSha", "run", "jobs"); return new GitHubReleaseGate(Object(request, "profile")).ValidateEvidence(Text(request, "sha"), Text(request, "mainSha"), Object(request, "run"), Array(request, "jobs"));
            case "resolve-evidence":
                Only(request, "profile", "sha", "ciRunId", "waitSeconds", "pollSeconds");
                return await new GitHubReleaseGate(Object(request, "profile")).ResolveEvidenceAsync(github, Text(request, "sha"), Integer(request, "ciRunId", 0), Int32(request, "waitSeconds", 0), Int32(request, "pollSeconds", 45), cancellationToken);
            case "classify-risk":
            {
                Only(request, "releaseOperation", "changedFiles", "policy", "tierOverride");
                if (!request.ContainsKey("changedFiles")) throw Json.Error("DELIVERY_PROTOCOL_INVALID");
                var files = request["changedFiles"] is null ? null : Strings(Array(request, "changedFiles"));
                return ReleaseRisk.Classify(Text(request, "releaseOperation"), files, Object(request, "policy"), OptionalText(request, "tierOverride", "auto"));
            }
            case "risk-tiers":
                Only(request); return new JsonArray(ReleaseRisk.TierOrder.Select(tier => (JsonNode?)JsonValue.Create(tier)).ToArray());
            case "admit-environment":
                Only(request, "result", "admittedBy"); return ReleaseRisk.AdmitEnvironment(Object(request, "result"), OptionalText(request, "admittedBy", "owner"));
            case "match-glob":
                Only(request, "glob", "path"); return JsonValue.Create(ReleaseRisk.Glob(Text(request, "glob")).IsMatch(Text(request, "path")));
            case "scaffold":
            {
                Only(request, "directory", "profile", "validation");
                var validation = Array(request, "validation").Select(row => row is JsonArray argv ? (IReadOnlyList<string>)Strings(argv) : throw Json.Error("DELIVERY_PROTOCOL_INVALID")).ToArray();
                DeliveryScaffold.Create(Text(request, "directory"), Object(request, "profile"), validation); return new JsonObject();
            }
            default: throw Json.Error("DELIVERY_PROTOCOL_INVALID");
        }
    }

    private static void Only(JsonObject request, params string[] keys) => Keys(request, keys.Append("operation").ToArray());
    private static void Keys(JsonObject value, params string[] keys) { if (value.Any(pair => !keys.Contains(pair.Key, StringComparer.Ordinal))) throw Json.Error("DELIVERY_PROTOCOL_INVALID"); }
    private static string Text(JsonObject value, string key) => value[key] is JsonValue text && text.TryGetValue<string>(out var result) ? result : throw Json.Error("DELIVERY_PROTOCOL_INVALID");
    private static string OptionalText(JsonObject value, string key, string fallback) => value.ContainsKey(key) ? Text(value, key) : fallback;
    private static JsonObject Object(JsonObject value, string key) => value[key] as JsonObject ?? throw Json.Error("DELIVERY_PROTOCOL_INVALID");
    private static JsonArray Array(JsonObject value, string key) => value[key] as JsonArray ?? throw Json.Error("DELIVERY_PROTOCOL_INVALID");
    private static string[] Strings(JsonArray values) => values.Select(node => node is JsonValue text && text.TryGetValue<string>(out var value) ? value : throw Json.Error("DELIVERY_PROTOCOL_INVALID")).ToArray();
    private static long Integer(JsonObject value, string key, long fallback) => !value.ContainsKey(key) ? fallback : Json.Integer(value[key]) is var number && number >= 0 ? number : throw Json.Error("DELIVERY_PROTOCOL_INVALID");
    private static int Int32(JsonObject value, string key, int fallback) => Integer(value, key, fallback) is var number && number <= int.MaxValue ? (int)number : throw Json.Error("DELIVERY_PROTOCOL_INVALID");

    private sealed class Channel(TextReader input, TextWriter output, CancellationToken cancellationToken)
    {
        // Console.In can fill a character block past an already buffered LF when
        // multibyte UTF-8 shifts its byte/character counts. Read one character so
        // an open request/response pipe never needs future bytes to finish a line.
        private readonly char[] buffer = new char[1];
        private int offset, count;
        private long sequence;

        public async Task<JsonObject> ReadAsync()
        {
            var line = new StringBuilder();
            while (true)
            {
                if (offset == count)
                {
                    count = await input.ReadAsync(buffer.AsMemory(), cancellationToken); offset = 0;
                    if (count == 0) throw Json.Error("DELIVERY_PROTOCOL_CLOSED");
                }
                var start = offset;
                while (offset < count && buffer[offset] != '\n') offset++;
                line.Append(buffer, start, offset - start);
                if (line.Length > MaximumLineBytes + 1 || line.Length > MaximumLineBytes && line[^1] != '\r') throw Json.Error("DELIVERY_PROTOCOL_TOO_LARGE");
                if (offset == count) continue;
                offset++;
                if (line.Length > 0 && line[^1] == '\r') line.Length--;
                var text = line.ToString();
                if (Encoding.UTF8.GetByteCount(text) > MaximumLineBytes) throw Json.Error("DELIVERY_PROTOCOL_TOO_LARGE");
                try
                {
                    using var document = JsonDocument.Parse(text);
                    UniqueKeys(document.RootElement);
                    return JsonNode.Parse(text) as JsonObject ?? throw Json.Error("DELIVERY_PROTOCOL_INVALID");
                }
                catch (JsonException) { throw Json.Error("DELIVERY_PROTOCOL_INVALID"); }
            }
        }

        private static void UniqueKeys(JsonElement value)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (!keys.Add(property.Name)) throw Json.Error("DELIVERY_PROTOCOL_INVALID");
                    UniqueKeys(property.Value);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) UniqueKeys(item);
        }

        public async Task WriteAsync(JsonObject message)
        {
            var text = message.ToJsonString();
            if (Encoding.UTF8.GetByteCount(text) > MaximumLineBytes) throw Json.Error("DELIVERY_PROTOCOL_TOO_LARGE");
            await output.WriteLineAsync(text.AsMemory(), cancellationToken); await output.FlushAsync(cancellationToken);
        }

        public async Task<JsonNode> RequestAsync(string path)
        {
            if (!path.StartsWith("/repos/", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal) || path.Any(c => char.IsControl(c) || c == '\\')) throw Json.Error("GITHUB_EVIDENCE_PATH_INVALID");
            var id = ++sequence;
            await WriteAsync(new JsonObject { ["request"] = id, ["path"] = path });
            var response = await ReadAsync(); Keys(response, "request", "value", "error");
            if (Json.Integer(response["request"]) != id) throw Json.Error("DELIVERY_PROTOCOL_OUT_OF_ORDER");
            if (response.Count != 2 || response.ContainsKey("value") == response.ContainsKey("error")) throw Json.Error("DELIVERY_PROTOCOL_INVALID");
            if (response.ContainsKey("error"))
            {
                if (!response.True("error")) throw Json.Error("DELIVERY_PROTOCOL_INVALID");
                throw Json.Error("GITHUB_EVIDENCE_UNAVAILABLE");
            }
            return response["value"]?.DeepClone() ?? throw Json.Error("DELIVERY_PROTOCOL_INVALID");
        }
    }
}
