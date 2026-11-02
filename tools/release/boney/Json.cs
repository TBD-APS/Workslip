using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Boney.Delivery;

internal static class Json
{
    public static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    public static string Text(this JsonNode? node, string key) => node?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";
    public static long Number(this JsonNode? node, string key) => Integer(node?[key]);
    public static long Integer(JsonNode? node) => node is JsonValue value && value.GetValueKind() == JsonValueKind.Number
        && decimal.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
        && number == decimal.Truncate(number) && number is >= -9007199254740991 and <= 9007199254740991 ? (long)number : -1;
    public static bool True(this JsonNode? node, string key) => node?[key] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
    public static JsonObject Copy(this JsonObject node) => (JsonObject)node.DeepClone();
    public static bool Matches(string value, string pattern) => Regex.IsMatch(value, "\\A(?:" + pattern + ")\\z", RegexOptions.CultureInvariant);
    public static bool Sha(string value) => Matches(value, "[a-f0-9]{40}");
    public static bool Slug(string value) => Matches(value, "[a-z][a-z0-9-]{0,63}");
    public static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    public static InvalidOperationException Error(string code) => new(code);
    public static JsonObject Object(string text) => JsonNode.Parse(text) as JsonObject ?? throw Error("JSON_OBJECT_REQUIRED");

    public static async Task<string> CommandAsync(string file, IEnumerable<string> args, string directory, string? input = null, CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo(file) { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var child = Process.Start(start) ?? throw Error("PROCESS_START_FAILED");
        var output = child.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = child.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            if (input is not null) await child.StandardInput.WriteAsync(input.AsMemory(), cancellationToken);
            child.StandardInput.Close();
            await child.WaitForExitAsync(cancellationToken);
            await error; // Drain diagnostics without exposing provider output or credentials.
            if (child.ExitCode != 0) throw Error("PROCESS_FAILED");
            return await output;
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(CancellationToken.None); }
        }
    }
}
