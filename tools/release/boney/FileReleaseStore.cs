using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace Boney.Delivery;

// A retained POSIX volume is required. Directory syncs make publication durable before acknowledgement.
public sealed class FileReleaseStore(string directory) : IReleaseStore
{
    [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int fsync(int descriptor);
    [DllImport("libc", SetLastError = true)] private static extern int close(int descriptor);

    private static void SyncDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The durable file store requires a POSIX filesystem.");
        var descriptor = open(path, 0);
        if (descriptor < 0) throw new IOException("Directory open failed.");
        try { if (fsync(descriptor) != 0) throw new IOException("Directory sync failed."); }
        finally { close(descriptor); }
    }

    private static void CreateDirectory(string path)
    {
        if (Directory.Exists(path)) return;
        CreateDirectory(Path.GetDirectoryName(path)!);
        if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        SyncDirectory(path); SyncDirectory(Path.GetDirectoryName(path)!);
    }

    internal static void WriteNew(string path, JsonObject value)
    {
        var bytes = Encoding.UTF8.GetBytes(value.ToJsonString(Json.Pretty) + "\n");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var file = new FileStream(path, options);
        file.Write(bytes); file.Flush(flushToDisk: true);
    }

    public async Task<JsonObject> WithLockAsync(ReleaseIdentity identity, Func<IReleaseSession, Task<JsonObject>> action)
    {
        if (!Json.Slug(identity.Product) || !Json.Slug(identity.Environment) || !Json.Sha(identity.Revision)) throw Json.Error("RELEASE_IDENTITY_INVALID");
        var scope = Path.Combine(Path.GetFullPath(directory), identity.Product, identity.Environment);
        CreateDirectory(scope);
        var lockPath = Path.Combine(scope, "active.lock");
        try { WriteNew(lockPath, new JsonObject { ["product"] = identity.Product, ["environment"] = identity.Environment, ["revision"] = identity.Revision, ["pid"] = Environment.ProcessId, ["startedAt"] = DateTimeOffset.UtcNow }); }
        catch (IOException) when (File.Exists(lockPath)) { throw Json.Error("RELEASE_LOCKED"); }
        try
        {
            SyncDirectory(scope);
            var receipts = Path.Combine(scope, identity.Revision); CreateDirectory(receipts);
            var files = Directory.GetFileSystemEntries(receipts).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
            if (files.Where((file, index) => file != $"{index + 1:D6}.json").Any()) throw Json.Error("RELEASE_RECEIPTS_INVALID");
            JsonObject? current = null;
            if (files.Length > 0)
            {
                try { current = Json.Object(await File.ReadAllTextAsync(Path.Combine(receipts, files[^1]!))); }
                catch { throw Json.Error("RELEASE_RECONCILIATION_REQUIRED"); }
                if (current.Number("schemaVersion") != 1 || current.Number("sequence") != files.Length || current.Text("product") != identity.Product || current.Text("environment") != identity.Environment || current.Text("revision") != identity.Revision) throw Json.Error("RELEASE_RECEIPTS_INVALID");
            }
            return await action(new Session(identity, receipts, files.Length, current));
        }
        finally { File.Delete(lockPath); SyncDirectory(scope); }
    }

    private sealed class Session(ReleaseIdentity identity, string directory, int sequence, JsonObject? current) : IReleaseSession
    {
        public JsonObject? Load() => current?.Copy();
        public Task<JsonObject> SaveAsync(JsonObject state)
        {
            var receipt = state.Copy(); receipt["product"] = identity.Product; receipt["environment"] = identity.Environment; receipt["revision"] = identity.Revision;
            receipt["sequence"] = sequence + 1; receipt["recordedAt"] = DateTimeOffset.UtcNow;
            var pending = Path.Combine(directory, ".pending-" + Guid.NewGuid());
            WriteNew(pending, receipt);
            // File.Move refuses replacement. A crash leaves a pending file requiring reconciliation.
            File.Move(pending, Path.Combine(directory, $"{sequence + 1:D6}.json"), overwrite: false);
            SyncDirectory(directory);
            sequence++; current = receipt;
            return Task.FromResult(receipt.Copy());
        }
    }
}
