using System.Text;
using System.Text.Json.Nodes;

namespace Boney.Delivery;

public static class DeliverySnapshot
{
    public static IReadOnlyList<string> RuntimeFiles { get; } = Array.AsReadOnly(new[] {
        "Boney.Delivery.csproj", "Json.cs", "DeliveryProfile.cs", "GitHubReleaseGate.cs", "ReleasePipeline.cs", "FileReleaseStore.cs", "ReleaseRisk.cs", "TrackedSources.cs", "DeliverySnapshot.cs", "DeliveryScaffold.cs", "ReferenceRelease.cs", "DeliveryCommandLine.cs", "CSharpProcessAdapter.cs", "Program.cs", "DeliveryPolicyProtocol.cs"
    });

    public static JsonObject Verify(string directory)
    {
        try
        {
            var manifest = Json.Object(File.ReadAllText(Path.Combine(directory, "boney.lock.json")));
            if (manifest.Number("schemaVersion") != 1 || manifest.Text("sourceRepository") != "TBD-APS/boney" || !Json.Sha(manifest.Text("sourceRevision")) || manifest["files"] is not JsonObject hashes
                || !hashes.Select(pair => pair.Key).Order(StringComparer.Ordinal).SequenceEqual(RuntimeFiles.Order(StringComparer.Ordinal))) throw Json.Error("DELIVERY_SNAPSHOT_INVALID");
            var entries = Directory.GetFileSystemEntries(directory).Where(path => Path.GetFileName(path) is not ("bin" or "obj")).Select(Path.GetFileName).Order(StringComparer.Ordinal);
            if (!entries.SequenceEqual(RuntimeFiles.Append("boney.lock.json").Order(StringComparer.Ordinal))) throw Json.Error("DELIVERY_SNAPSHOT_INVALID");
            foreach (var name in RuntimeFiles)
            {
                var file = Path.Combine(directory, name);
                if (!File.Exists(file) || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 || !Json.Matches(hashes.Text(name), "[a-f0-9]{64}") || Json.Hash(File.ReadAllBytes(file)) != hashes.Text(name)) throw Json.Error("DELIVERY_SNAPSHOT_INVALID");
            }
            return manifest;
        }
        catch { throw Json.Error("DELIVERY_SNAPSHOT_INVALID"); }
    }

    public static async Task ExportAsync(string target, string revision, string? root = null)
    {
        if (!Json.Sha(revision) || Directory.Exists(target) || File.Exists(target)) throw Json.Error("DELIVERY_EXPORT_INVALID");
        root ??= (await Json.CommandAsync("git", ["rev-parse", "--show-toplevel"], Environment.CurrentDirectory)).Trim();
        var contents = new Dictionary<string, byte[]>(); var hashes = new JsonObject();
        foreach (var name in RuntimeFiles)
        {
            var text = await Json.CommandAsync("git", ["show", $"{revision}:src/Boney.Delivery/{name}"], root);
            var bytes = Encoding.UTF8.GetBytes(text); contents[name] = bytes; hashes[name] = Json.Hash(bytes);
        }
        Directory.CreateDirectory(target);
        foreach (var (name, bytes) in contents) { using var file = new FileStream(Path.Combine(target, name), FileMode.CreateNew); await file.WriteAsync(bytes); }
        FileReleaseStore.WriteNew(Path.Combine(target, "boney.lock.json"), new JsonObject { ["schemaVersion"] = 1, ["sourceRepository"] = "TBD-APS/boney", ["sourceRevision"] = revision, ["files"] = hashes });
        Verify(target);
    }
}
