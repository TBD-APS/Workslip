using System.Text.Json.Nodes;

namespace Boney.Delivery;

public static class DeliveryProfile
{
    public static JsonObject Validate(JsonObject input)
    {
        var profile = input.Copy();
        static void Require(bool valid) { if (!valid) throw Json.Error("DELIVERY_PROFILE_INVALID"); }
        static void Keys(JsonObject value, params string[] keys) => Require(value.All(pair => keys.Contains(pair.Key, StringComparer.Ordinal)));
        static void List(JsonNode? node, Func<JsonNode?, bool> predicate)
        {
            Require(node is JsonArray { Count: > 0 and <= 100 } values && values.All(predicate) && values.Select(v => v!.ToJsonString()).Distinct().Count() == values.Count);
        }
        Keys(profile, "schemaVersion", "product", "repository", "branch", "mergerIds", "events", "ci", "environments");
        Require(profile.Number("schemaVersion") == 1 && Json.Slug(profile.Text("product"))
            && Json.Matches(profile.Text("repository"), "[A-Za-z0-9_-][A-Za-z0-9_.-]*/[A-Za-z0-9_-][A-Za-z0-9_.-]*")
            && Json.Matches(profile.Text("branch"), "[A-Za-z0-9][A-Za-z0-9._/-]{0,100}")
            && !profile.Text("branch").Contains("..", StringComparison.Ordinal)
            && profile.Text("branch").Split('/').All(part => part.Length > 0 && !part.EndsWith('.') && !part.EndsWith(".lock", StringComparison.Ordinal)));
        List(profile["mergerIds"], node => Json.Integer(node) > 0);
        Require(((JsonArray)profile["mergerIds"]!).Select(Json.Integer).Distinct().Count() == ((JsonArray)profile["mergerIds"]!).Count);
        List(profile["events"], node => node is JsonValue value && value.TryGetValue<string>(out var name) && name is "push" or "workflow_dispatch");
        if (profile.ContainsKey("ci"))
        {
            Require(profile["ci"] is JsonObject);
            var ci = (JsonObject)profile["ci"]!;
            Keys(ci, "workflow", "requiredJobs");
            Require(Json.Matches(ci.Text("workflow"), @"[A-Za-z0-9_-][A-Za-z0-9_.-]*\.ya?ml") && !ci.Text("workflow").Contains("..", StringComparison.Ordinal));
            List(ci["requiredJobs"], node => node is JsonValue value && value.TryGetValue<string>(out var name) && name.Length is > 0 and < 150 && name.Trim() == name && !name.Any(c => c is '\r' or '\n'));
        }
        Require(profile["environments"] is JsonObject { Count: > 0 });
        foreach (var (name, node) in (JsonObject)profile["environments"]!)
        {
            Require(node is JsonObject);
            var target = (JsonObject)node!;
            Keys(target, "githubEnvironment", "migration", "recovery");
            Require(Json.Slug(name) && Json.Slug(target.Text("githubEnvironment")) && target.Text("migration") is "none" or "expand-contract" && target.Text("recovery") is "manual" or "automatic");
        }
        return profile;
    }

    public static JsonObject Load(string path) => Validate(Json.Object(File.ReadAllText(path)));
}
