using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Boney.Delivery;

public static class ReleaseRisk
{
    private static readonly string[] Tiers = ["low", "medium", "high"];
    public static IReadOnlyList<string> TierOrder { get; } = Array.AsReadOnly(Tiers);
    private static int Rank(string tier) => Array.IndexOf(Tiers, tier) is var rank && rank >= 0 ? rank : throw Json.Error("RISK_TIER_INVALID");
    public static Regex Glob(string glob)
    {
        if (string.IsNullOrEmpty(glob)) throw Json.Error("RISK_GLOB_INVALID");
        var pattern = new StringBuilder("\\A");
        for (var i = 0; i < glob.Length; i++)
        {
            if (glob[i] != '*') { pattern.Append(Regex.Escape(glob[i].ToString())); continue; }
            if (i + 1 < glob.Length && glob[i + 1] == '*')
            {
                i++;
                if (i + 1 < glob.Length && glob[i + 1] == '/') { i++; pattern.Append("(?:.*/)?"); }
                else pattern.Append(".*");
            }
            else pattern.Append("[^/]*");
        }
        return new Regex(pattern.Append("\\z").ToString(), RegexOptions.CultureInvariant);
    }

    public static JsonObject Classify(string operation, IReadOnlyList<string>? changedFiles, JsonObject policy, string tierOverride = "auto")
    {
        if (policy["operations"] is not JsonObject operations || policy["tiers"] is not JsonObject tiers || policy["pathRules"] is not JsonArray rules) throw Json.Error("RISK_POLICY_REQUIRED");
        Rank(policy.Text("unmatchedPathTier")); Rank(policy.Text("unknownBaselineTier"));
        foreach (var tier in Tiers)
        {
            var spec = tiers[tier] as JsonObject ?? throw Json.Error("RISK_POLICY_INVALID");
            if (string.IsNullOrWhiteSpace(spec.Text("environment")) || spec["selfHealEnvironment"] is not null && string.IsNullOrWhiteSpace(spec.Text("selfHealEnvironment")) || spec.Number("waitTimerMinutes") < 0
                || spec["confirmationSuffix"] is not JsonValue suffix || !suffix.TryGetValue<string>(out _)
                || spec["requiresOwnerApproval"] is JsonNode approval && (approval is not JsonValue flag || !flag.TryGetValue<bool>(out _))) throw Json.Error("RISK_POLICY_INVALID");
        }
        foreach (var entry in operations)
        {
            if (entry.Value is not JsonObject op || op["note"] is JsonNode note && (note is not JsonValue text || !text.TryGetValue<string>(out _))) throw Json.Error("RISK_POLICY_INVALID");
            if (op.ContainsKey("floor")) Rank(op.Text("floor"));
        }
        var matchers = rules.Select(node =>
        {
            if (node is not JsonObject rule || string.IsNullOrWhiteSpace(rule.Text("reason")) || rule["globs"] is not JsonArray { Count: > 0 } globs) throw Json.Error("RISK_POLICY_INVALID");
            Rank(rule.Text("tier"));
            return (Rule: rule, Patterns: globs.Select(glob => Glob(glob?.GetValue<string>() ?? "")).ToArray());
        }).ToArray();
        if (changedFiles?.Any(file => string.IsNullOrEmpty(file) || file.StartsWith('/') || file.Any(c => char.IsControl(c) || c == '\\') || file.Split('/').Any(part => part is "" or "." or "..")) == true) throw Json.Error("RISK_PATH_INVALID");
        var reasons = new JsonArray(); var rank = 0;
        void Raise(string tier) => rank = Math.Max(rank, Rank(tier));
        if (operations[operation] is not JsonObject selected) { rank = 2; reasons.Add($"operation '{operation}' is not in the policy; unknown operations are high risk"); }
        else if (selected.ContainsKey("floor")) { Raise(selected.Text("floor")); if (rank > 0) reasons.Add($"operation '{operation}' has a floor of {Tiers[rank]} ({selected.Text("note")})"); }
        if (changedFiles is null) { Raise(policy.Text("unknownBaselineTier")); reasons.Add($"no successful previous run of this operation was found; treated as {policy.Text("unknownBaselineTier")}"); }
        else
        {
            var hits = new Dictionary<int, List<string>>(); var unmatched = new List<string>();
            foreach (var file in changedFiles)
            {
                var index = Array.FindIndex(matchers, rule => rule.Patterns.Any(pattern => pattern.IsMatch(file)));
                if (index < 0) unmatched.Add(file);
                else { if (!hits.TryGetValue(index, out var files)) hits[index] = files = []; files.Add(file); }
            }
            foreach (var (index, files) in hits) { var rule = matchers[index].Rule; Raise(rule.Text("tier")); reasons.Add($"{rule.Text("tier")}: {rule.Text("reason")} — {files.Count} file(s), e.g. {files[0]}"); }
            if (unmatched.Count > 0) { Raise(policy.Text("unmatchedPathTier")); reasons.Add($"{policy.Text("unmatchedPathTier")}: {unmatched.Count} file(s) matched no rule, e.g. {unmatched[0]}"); }
            if (changedFiles.Count == 0) reasons.Add("no files changed since the last successful run of this operation");
        }
        if (!string.IsNullOrEmpty(tierOverride) && tierOverride != "auto")
        {
            var requested = Rank(tierOverride);
            if (requested > rank) { reasons.Add($"raised from {Tiers[rank]} to {tierOverride} by operator override"); rank = requested; }
            else if (requested < rank) reasons.Add($"operator override '{tierOverride}' ignored: the computed tier {Tiers[rank]} cannot be lowered");
        }
        var result = (JsonObject)tiers[Tiers[rank]]!.DeepClone(); result["tier"] = Tiers[rank]; result["reasons"] = reasons;
        if (!result.ContainsKey("requiresOwnerApproval")) result["requiresOwnerApproval"] = true;
        if (!result.ContainsKey("selfHealEnvironment")) result["selfHealEnvironment"] = null;
        return result;
    }

    public static JsonObject AdmitEnvironment(JsonObject result, string admittedBy = "owner")
    {
        if (string.IsNullOrWhiteSpace(result.Text("environment")) || result["requiresOwnerApproval"] is not JsonValue flag || !flag.TryGetValue<bool>(out _)
            || result["selfHealEnvironment"] is not null && string.IsNullOrWhiteSpace(result.Text("selfHealEnvironment"))) throw Json.Error("RISK_ENVIRONMENT_INVALID");
        if (admittedBy == "owner") return new JsonObject { ["environment"] = result.Text("environment"), ["requiresOwnerApproval"] = result.True("requiresOwnerApproval") };
        if (admittedBy == "self-heal" && result.Text("selfHealEnvironment").Length > 0) return new JsonObject { ["environment"] = result.Text("selfHealEnvironment"), ["requiresOwnerApproval"] = false };
        throw Json.Error("RISK_SELF_HEAL_REFUSED");
    }
}
