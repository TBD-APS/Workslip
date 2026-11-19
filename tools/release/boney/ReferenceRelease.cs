using System.Text.Json.Nodes;

namespace Boney.Delivery;

public static class ReferenceRelease
{
    public static string RequireRevision(string value) => Json.Sha(value) ? value : throw Json.Error("A full lowercase source commit SHA is required.");
    private static async Task<string> CleanSource(string root)
    {
        if ((await Json.CommandAsync("git", ["status", "--porcelain=v1", "--untracked-files=normal"], root)).Length > 0) throw Json.Error("Publish requires a clean committed source tree.");
        return RequireRevision((await Json.CommandAsync("git", ["rev-parse", "HEAD"], root)).Trim());
    }
    public static JsonArray Inventory(string directory)
    {
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Walk(string path)
        {
            foreach (var entry in Directory.GetFileSystemEntries(path))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw Json.Error("A release package cannot contain symbolic links.");
                if ((attributes & FileAttributes.Directory) != 0) Walk(entry);
                else { var name = Path.GetRelativePath(directory, entry).Replace(Path.DirectorySeparatorChar, '/'); if (name != "release.json") files[name] = Json.Hash(File.ReadAllBytes(entry)); }
            }
        }
        Walk(directory);
        return new JsonArray(files.Select(pair => (JsonNode)new JsonObject { ["path"] = pair.Key, ["sha256"] = pair.Value }).ToArray());
    }
    public static JsonObject VerifyPackage(string directory, string expectedRevision)
    {
        RequireRevision(expectedRevision);
        var manifest = Json.Object(File.ReadAllText(Path.Combine(directory, "release.json")));
        if (manifest.Number("schemaVersion") != 1 || manifest.Text("product") != "boney-reference" || manifest["source"].Text("repository") != "TBD-APS/boney" || manifest["source"].Text("revision") != expectedRevision || manifest["files"] is not JsonArray files) throw Json.Error("The package source manifest does not match.");
        foreach (var required in new[] { "Boney.Reference.dll", "Boney.Reference.runtimeconfig.json", "wwwroot/index.html", "wwwroot/app.js" })
            if (!files.Any(file => file.Text("path") == required)) throw Json.Error("The release package lacks " + required);
        if (!JsonNode.DeepEquals(Inventory(directory), files)) throw Json.Error("The package files do not match their recorded hashes.");
        return manifest;
    }
    public static async Task<JsonObject> PublishAsync(string root, string? directory = null)
    {
        var revision = await CleanSource(root);
        directory = Path.GetFullPath(directory ?? Path.Combine(root, ".evidence/reference-release", revision, "package"));
        if (Directory.Exists(directory)) return VerifyPackage(directory, revision);
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        var temporary = Path.Combine(Path.GetDirectoryName(directory)!, ".publish-" + Guid.NewGuid()); Directory.CreateDirectory(temporary);
        try
        {
            await Json.CommandAsync("dotnet", ["publish", "examples/Boney.Reference/Boney.Reference.csproj", "--configuration", "Release", "--output", temporary, "--nologo", "-p:BoneySourceRevision=" + revision], root);
            if (await CleanSource(root) != revision) throw Json.Error("Source changed while publishing.");
            var manifest = new JsonObject { ["schemaVersion"] = 1, ["product"] = "boney-reference", ["source"] = new JsonObject { ["repository"] = "TBD-APS/boney", ["revision"] = revision }, ["files"] = Inventory(temporary) };
            FileReleaseStore.WriteNew(Path.Combine(temporary, "release.json"), manifest); VerifyPackage(temporary, revision);
            Directory.Move(temporary, directory); return manifest;
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true); }
    }
    public static async Task<JsonObject> ObserveAsync(string baseUrl, string revision, HttpClient? client = null)
    {
        RequireRevision(revision);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var origin) || origin.UserInfo.Length > 0 || origin.Query.Length > 0 || origin.Fragment.Length > 0 || origin.AbsolutePath != "/" || origin.Scheme != "https" && !(origin.IsLoopback && origin.Scheme == "http")) throw Json.Error("Use an HTTPS origin without credentials, path or query; local proof permits loopback HTTP.");
        using var ownedClient = client is null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) } : null;
        client ??= ownedClient!;
        async Task<JsonObject> Get(string path)
        {
            using var response = await client.GetAsync(new Uri(origin, path), HttpCompletionOption.ResponseHeadersRead);
            if ((int)response.StatusCode != 200) throw Json.Error(path + " did not return HTTP 200.");
            await response.Content.LoadIntoBufferAsync(65536);
            return Json.Object(await response.Content.ReadAsStringAsync());
        }
        if ((await Get("/health")).Text("status") != "ok") throw Json.Error("The readiness contract failed.");
        var identity = await Get("/api/release");
        if (identity.Number("schemaVersion") != 1 || identity.Text("product") != "boney-reference" || identity.Text("sourceRevision") != revision) throw Json.Error("The running source does not match the expected release.");
        using var anonymous = await client.GetAsync(new Uri(origin, "/api/organizations"), HttpCompletionOption.ResponseHeadersRead);
        if ((int)anonymous.StatusCode != 401) throw Json.Error("Anonymous organization access was not refused.");
        return new JsonObject { ["schemaVersion"] = 1, ["kind"] = "reference-runtime-observation", ["product"] = "boney-reference", ["sourceRevision"] = revision,
            ["baseUrl"] = origin.AbsoluteUri, ["observedAt"] = DateTimeOffset.UtcNow, ["checks"] = new JsonArray("dependency-readiness", "running-source", "anonymous-access-refused"),
            ["authenticatedJourney"] = "not-run", ["migration"] = "not-observed", ["deployment"] = "not-observed" };
    }
    public static void SaveReceipt(string file, JsonObject value)
    {
        file = Path.GetFullPath(file); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary = file + "." + Guid.NewGuid() + ".tmp";
        try { FileReleaseStore.WriteNew(temporary, value); File.Move(temporary, file, overwrite: false); }
        finally { File.Delete(temporary); }
    }
    public static async Task<JsonObject> ContainerProofAsync(string revision, string image, string package, string temporary)
    {
        RequireRevision(revision); var manifest = VerifyPackage(package, revision);
        if (!JsonNode.DeepEquals(manifest, Json.Object(File.ReadAllText(Path.Combine(temporary, "release.json"))))
            || Json.Hash(File.ReadAllBytes(Path.Combine(temporary, "Boney.Reference.dll"))) != ((JsonArray)manifest["files"]!).First(file => file.Text("path") == "Boney.Reference.dll").Text("sha256")) throw Json.Error("CONTAINER_BYTES_INVALID");
        async Task<string> Inspect(string expression) => (await Json.CommandAsync("docker", ["image", "inspect", "--format", expression, image], Environment.CurrentDirectory)).Trim();
        if (await Inspect("{{.Config.User}}") != "app" || await Inspect("{{.Os}}/{{.Architecture}}") != "linux/amd64" || !JsonNode.DeepEquals(JsonNode.Parse(await Inspect("{{json .Config.Entrypoint}}")), new JsonArray("dotnet", "Boney.Reference.dll")) || await Inspect("{{index .Config.Labels \"org.opencontainers.image.revision\"}}") != revision) throw Json.Error("CONTAINER_IDENTITY_INVALID");
        var result = new JsonObject { ["schemaVersion"] = 1, ["kind"] = "reference-container-build", ["sourceRevision"] = revision, ["image"] = image, ["imageId"] = await Inspect("{{.Id}}"), ["platform"] = "linux/amd64", ["user"] = "app",
            ["runtimes"] = new JsonArray(File.ReadAllLines(Path.Combine(temporary, "runtimes.txt")).Select(line => JsonValue.Create(line)).ToArray()),
            ["checks"] = new JsonArray("package-inventory", "image-binary-bytes", "source-label", "non-root-user", "entrypoint", "dotnet-runtime-start"), ["applicationHealth"] = "not-observed", ["deployment"] = "not-run" };
        SaveReceipt(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(package))!, $"container-proof-{Guid.NewGuid()}.json"), result); return result;
    }
}
