using System.Text.Json;
using System.Text.Json.Nodes;

namespace Boney.Delivery;

// A reviewed .NET 10 file-based app. Stages exchange JSON on stdin/stdout.
public sealed class CSharpProcessAdapter : IDeliveryAdapter, IMigrationAdapter, IRecoveryAdapter
{
    private readonly string file;
    private readonly byte[] source;

    public CSharpProcessAdapter(string path)
    {
        if (Path.GetExtension(path) != ".cs" || File.ReadLines(path).Any(line => line.TrimStart().StartsWith("#:", StringComparison.Ordinal))) throw Json.Error("DELIVERY_ADAPTER_INVALID");
        file = Path.GetFullPath(path); source = File.ReadAllBytes(file);
    }

    private async Task<JsonObject> Call(string stage, ReleaseArguments args)
    {
        await TrackedSources.RequireAsync(args.Context.Sha, [file], Path.GetDirectoryName(file));
        if (!source.AsSpan().SequenceEqual(File.ReadAllBytes(file))) throw Json.Error("DELIVERY_SOURCE_INVALID");
        // Build an isolated copy: provider code cannot replace the next stage's source,
        // and product Directory.Build.props/targets do not enter this file-based app.
        var temporary = Directory.CreateTempSubdirectory("boney-adapter-");
        try
        {
            var copy = Path.Combine(temporary.FullName, "adapter.cs"); await File.WriteAllBytesAsync(copy, source);
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(30));
            var input = JsonSerializer.Serialize(args, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var output = await Json.CommandAsync("dotnet", ["run", "--file", copy, "--no-launch-profile", "--", stage], Environment.CurrentDirectory, input, deadline.Token);
            return Json.Object(output);
        }
        finally { temporary.Delete(recursive: true); }
    }

    public Task<JsonObject> PrepareAsync(ReleaseArguments args) => Call("prepare", args);
    public Task<JsonObject> MigrateAsync(ReleaseArguments args) => Call("migrate", args);
    public Task<JsonObject> DeployAsync(ReleaseArguments args) => Call("deploy", args);
    public Task<JsonObject> ObserveAsync(ReleaseArguments args) => Call("observe", args);
    public Task<JsonObject> RecoverAsync(ReleaseArguments args) => Call("recover", args);
}
