using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Workslip.Infrastructure.Diagnostics;

public static partial class DiagnosticsSanitizer
{
    private const int MaxMessageLength = 600;
    private const int MaxFieldLength = 160;

    public static string SanitizeMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Ukendt fejl";

        var sanitized = value
            .Replace('\r', ' ')
            .Replace('\n', ' ');

        sanitized = CredentialPattern().Replace(sanitized, "$1[REDACTED]");
        sanitized = BearerPattern().Replace(sanitized, "Bearer [REDACTED]");
        sanitized = QuerySecretPattern().Replace(sanitized, "$1[REDACTED]");
        sanitized = QuotedSecretPattern().Replace(sanitized, "$1$3[REDACTED]$3");
        sanitized = SecretPattern().Replace(sanitized, "$1[REDACTED]$3");
        sanitized = EmailPattern().Replace(sanitized, "[REDACTED_EMAIL]");
        sanitized = PhonePattern().Replace(sanitized, "[REDACTED_PHONE]");
        sanitized = GuidPattern().Replace(sanitized, ":id");
        sanitized = LongTokenPattern().Replace(sanitized, "[REDACTED_TOKEN]");
        sanitized = WhitespacePattern().Replace(sanitized, " ").Trim();

        return Truncate(sanitized, MaxMessageLength);
    }

    public static string? SanitizeRoute(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var route = value.Split('?', '#')[0];
        route = GuidPattern().Replace(route, ":id");
        route = NumericSegmentPattern().Replace(route, "/:id");
        route = EmailPattern().Replace(route, "[REDACTED_EMAIL]");
        route = WhitespacePattern().Replace(route, " ").Trim();

        return string.IsNullOrWhiteSpace(route)
            ? null
            : Truncate(route, MaxFieldLength);
    }

    public static string? SanitizeField(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var sanitized = SanitizeMessage(value);
        return string.IsNullOrWhiteSpace(sanitized)
            ? null
            : Truncate(sanitized, MaxFieldLength);
    }

    public static string? SanitizeCorrelationId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        return SafeIdentifierPattern().IsMatch(trimmed)
            ? Truncate(trimmed, 128)
            : null;
    }

    public static string Fingerprint(params string?[] values)
    {
        var normalized = string.Join('|', values.Select(value => value?.Trim().ToLowerInvariant() ?? string.Empty));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    [GeneratedRegex("(([\\\"']?(?:access[_-]?token|refresh[_-]?token|id[_-]?token|client[_-]?secret|password|secret|api[_-]?key)[\\\"']?)\\s*[:=]\\s*)([\\\"'])(.*?)\\3", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuotedSecretPattern();

    [GeneratedRegex("(([\\\"']?(?:access[_-]?token|refresh[_-]?token|id[_-]?token|client[_-]?secret|password|secret|api[_-]?key)[\\\"']?)\\s*[:=]\\s*[\\\"']?)[^\\\"',}\\s]+([\\\"']?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretPattern();

    [GeneratedRegex("(authorization\\s*[:=]\\s*[\\\"']?(?:bearer|basic)\\s+)[^\\\"',}\\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CredentialPattern();

    [GeneratedRegex("bearer\\s+[a-z0-9._~+/=-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerPattern();

    [GeneratedRegex("([?&](?:access[_-]?token|refresh[_-]?token|id[_-]?token|authorization|api[_-]?key)=)[^&#\\s]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuerySecretPattern();

    [GeneratedRegex("\\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,}\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    [GeneratedRegex("(?:\\+?\\d[\\d\\s().-]{7,}\\d)", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();

    [GeneratedRegex("\\b[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GuidPattern();

    [GeneratedRegex("/[0-9]+(?=/|$)", RegexOptions.CultureInvariant)]
    private static partial Regex NumericSegmentPattern();

    [GeneratedRegex("\\b[A-Za-z0-9_-]{48,}\\b", RegexOptions.CultureInvariant)]
    private static partial Regex LongTokenPattern();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex("^[A-Za-z0-9._:/=-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdentifierPattern();
}
