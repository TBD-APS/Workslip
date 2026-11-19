using System.Text.Json.Nodes;

namespace Boney.Delivery;

public sealed record ReleaseIdentity(string Product, string Environment, string Revision);
public sealed record ReleaseArguments(ReleaseContext Context, string Environment, JsonObject Target, JsonObject? Candidate = null, JsonObject? Previous = null);
public interface IDeliveryAdapter
{
    Task<JsonObject> PrepareAsync(ReleaseArguments args);
    Task<JsonObject> DeployAsync(ReleaseArguments args);
    Task<JsonObject> ObserveAsync(ReleaseArguments args);
}
public interface IMigrationAdapter { Task<JsonObject> MigrateAsync(ReleaseArguments args); }
public interface IRecoveryAdapter { Task<JsonObject> RecoverAsync(ReleaseArguments args); }
public interface IReleaseSession
{
    JsonObject? Load();
    Task<JsonObject> SaveAsync(JsonObject state);
}
public interface IReleaseStore
{
    Task<JsonObject> WithLockAsync(ReleaseIdentity identity, Func<IReleaseSession, Task<JsonObject>> action);
}

public static class ReleasePipeline
{
    private static JsonObject Artifact(JsonNode? value, string revision)
    {
        var reference = value?["artifact"].Text("reference") ?? "";
        var digest = value?["artifact"].Text("digest") ?? "";
        if (!Json.Sha(revision) || value.Text("revision") != revision || !Json.Matches(digest, "[a-f0-9]{64}")
            || reference.Length > 400 || !reference.EndsWith($"sha256:{digest}", StringComparison.Ordinal)
            || reference.Any(c => char.IsWhiteSpace(c) || c is '?' or '#')) throw Json.Error("ARTIFACT_IDENTITY_INVALID");
        if (reference.Contains("://", StringComparison.Ordinal) && (!Uri.TryCreate(reference, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0)) throw Json.Error("ARTIFACT_IDENTITY_INVALID");
        return new JsonObject { ["revision"] = revision, ["artifact"] = value!["artifact"]!.DeepClone() };
    }

    private static void Observation(JsonNode value, JsonObject expected)
    {
        if (!value.True("healthy") || value.Text("revision") != expected.Text("revision") || value.Text("artifactDigest") != expected["artifact"].Text("digest")) throw Json.Error("OBSERVATION_IDENTITY_INVALID");
    }

    public static async Task<JsonObject> ExecuteAsync(JsonObject input, string environment, ReleaseContext context, IDeliveryAdapter adapter, IReleaseStore store, GitHubRequest request)
    {
        var profile = DeliveryProfile.Validate(input);
        if (profile["environments"]?[environment] is not JsonObject target
            || target.Text("migration") != "none" && adapter is not IMigrationAdapter
            || target.Text("recovery") == "automatic" && adapter is not IRecoveryAdapter) throw Json.Error("DELIVERY_ADAPTER_INVALID");
        var gate = new GitHubReleaseGate(profile);
        var identity = new ReleaseIdentity(profile.Text("product"), environment, context.Sha);
        return await store.WithLockAsync(identity, async session =>
        {
            var existing = session.Load();
            if (existing is not null)
            {
                if (existing.Number("schemaVersion") != 1 || existing.Text("status") != "released" || existing.Text("repository") != profile.Text("repository")
                    || existing.Text("product") != identity.Product || existing.Text("environment") != environment || existing.Text("revision") != context.Sha
                    || existing.Text("githubEnvironment") != target.Text("githubEnvironment")) throw Json.Error("RELEASE_RECONCILIATION_REQUIRED");
                try { Artifact(existing["candidate"], context.Sha); } catch { throw Json.Error("RELEASE_RECONCILIATION_REQUIRED"); }
                await gate.AuthorizeAsync(context, request);
                return existing;
            }
            var stage = "checking"; JsonObject? candidate = null, previous = null;
            var mutationStarted = false; var persistenceFailed = false;
            var state = new JsonObject { ["schemaVersion"] = 1, ["product"] = identity.Product, ["environment"] = environment, ["revision"] = context.Sha,
                ["repository"] = profile.Text("repository"), ["githubEnvironment"] = target.Text("githubEnvironment"), ["status"] = stage };
            async Task<JsonObject> Save(string status, JsonObject? extra = null)
            {
                if (extra is not null) foreach (var (key, value) in extra) state[key] = value?.DeepClone();
                state["status"] = status;
                try
                {
                    var receipt = await session.SaveAsync(state.Copy());
                    if (receipt is null || !state.All(pair => receipt.ContainsKey(pair.Key) && JsonNode.DeepEquals(pair.Value, receipt[pair.Key]))) throw Json.Error("RELEASE_RECEIPT_INVALID");
                    return receipt.Copy();
                }
                catch { persistenceFailed = true; throw Json.Error("RELEASE_RECONCILIATION_REQUIRED"); }
            }
            ReleaseArguments Args() => new(context, environment, target.Copy(), candidate?.Copy(), previous?.Copy());
            try
            {
                await Save("admitted", new JsonObject { ["admission"] = await gate.AuthorizeAsync(context, request) });
                stage = "preparing"; await Save(stage);
                var prepared = await adapter.PrepareAsync(Args());
                candidate = Artifact(prepared, context.Sha);
                if (target.Text("migration") == "expand-contract" && prepared["migration"].Text("compatibility") != "expand-contract") throw Json.Error("MIGRATION_POLICY_REFUSED");
                if (target.Text("recovery") == "automatic")
                {
                    previous = Artifact(prepared["previous"], prepared["previous"].Text("revision"));
                    if (previous.Text("revision") == context.Sha) throw Json.Error("RECOVERY_IDENTITY_INVALID");
                }
                var preparedState = new JsonObject { ["candidate"] = candidate.Copy() };
                if (previous is not null) preparedState["previous"] = previous.Copy();
                await Save("prepared", preparedState);
                if (target.Text("migration") != "none")
                {
                    await gate.AuthorizeAsync(context, request);
                    stage = "migrating"; await Save(stage);
                    var migration = await ((IMigrationAdapter)adapter).MigrateAsync(Args());
                    if (!migration.True("applied") || migration.Text("revision") != context.Sha) throw Json.Error("MIGRATION_NOT_CONFIRMED");
                    await Save("migrated");
                }
                await gate.AuthorizeAsync(context, request);
                stage = "deploying"; await Save(stage); mutationStarted = true;
                var deployed = await adapter.DeployAsync(Args());
                if (deployed.Text("revision") != candidate.Text("revision") || deployed.Text("artifactDigest") != candidate["artifact"].Text("digest")) throw Json.Error("DEPLOYMENT_IDENTITY_INVALID");
                stage = "observing"; await Save(stage);
                Observation(await adapter.ObserveAsync(Args()), candidate);
                return await Save("released");
            }
            catch
            {
                // A durable write with an uncertain acknowledgement must never cause a second mutation.
                if (persistenceFailed) throw Json.Error("RELEASE_RECONCILIATION_REQUIRED");
                if (mutationStarted && target.Text("recovery") == "automatic" && previous is not null)
                {
                    await Save("recovering", new JsonObject { ["failedStage"] = stage });
                    try
                    {
                        Observation(await ((IRecoveryAdapter)adapter).RecoverAsync(Args()), previous);
                        return await Save("rolled-back", new JsonObject { ["recoveredRevision"] = previous.Text("revision") });
                    }
                    catch
                    {
                        if (persistenceFailed) throw Json.Error("RELEASE_RECONCILIATION_REQUIRED");
                        return await Save("blocked", new JsonObject { ["reason"] = "RECOVERY_NOT_CONFIRMED" });
                    }
                }
                return await Save("blocked", new JsonObject { ["failedStage"] = stage, ["reason"] = mutationStarted ? "MANUAL_RECOVERY_REQUIRED" : "RELEASE_STEP_FAILED" });
            }
        });
    }
}
