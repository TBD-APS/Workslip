using System.Text.RegularExpressions;

namespace Boney.Delivery;

public static class TrackedSources
{
    public static async Task<string[]> RequireAsync(string revision, IEnumerable<string> paths, string? cwd = null)
    {
        cwd = Path.GetFullPath(cwd ?? Environment.CurrentDirectory);
        Task<string> Git(params string[] args) => Json.CommandAsync("git", args, cwd);
        if (!Json.Sha(revision) || (await Git("rev-parse", "HEAD")).Trim() != revision || (await Git("status", "--porcelain", "--untracked-files=normal")).Length != 0) throw Json.Error("DELIVERY_REQUIRES_CLEAN_EXACT_SOURCE");
        var root = (await Git("rev-parse", "--show-toplevel")).Trim();
        var sources = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || path.Any(c => c is '\r' or '\n' or '\0')) throw Json.Error("DELIVERY_SOURCE_INVALID");
                var absolute = Path.GetFullPath(path, cwd); var name = Path.GetRelativePath(root, absolute);
                if (name == ".." || name.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(name) || !File.Exists(absolute)) throw Json.Error("DELIVERY_SOURCE_INVALID");
                var current = root;
                foreach (var segment in name.Split(Path.DirectorySeparatorChar))
                {
                    current = Path.Combine(current, segment);
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw Json.Error("DELIVERY_SOURCE_INVALID");
                }
                var entry = await Git("ls-tree", "--full-tree", "-z", revision, "--", ":(literal)" + name);
                var match = Regex.Match(entry, "\\A(?:100644|100755) blob ([a-f0-9]{40})\\t([^\\0]+)\\0\\z");
                if (!match.Success || match.Groups[2].Value != name || (await Git("hash-object", "--no-filters", "--", absolute)).Trim() != match.Groups[1].Value) throw Json.Error("DELIVERY_SOURCE_INVALID");
                sources.Add(absolute);
            }
            catch { throw Json.Error("DELIVERY_SOURCE_INVALID"); }
        }
        if (sources.Count == 0) throw Json.Error("DELIVERY_SOURCE_INVALID");
        return sources.ToArray();
    }
}
