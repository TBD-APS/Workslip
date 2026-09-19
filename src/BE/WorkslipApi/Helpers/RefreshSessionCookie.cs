using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Hosting;

namespace Workslip.Api.Helpers;

internal static class RefreshSessionCookie
{
    private const string SecureName = "__Host-workslip-refresh";
    private const string DevelopmentName = "workslip-refresh";

    internal static string? Read(HttpRequest request) =>
        request.Cookies[SecureName] ?? request.Cookies[DevelopmentName];

    internal static void Write(HttpContext context, string token, DateTimeOffset expiresAt)
    {
        var secure = !context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment();
        context.Response.Cookies.Append(secure ? SecureName : DevelopmentName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = expiresAt,
            IsEssential = true
        });
    }

    internal static void Clear(HttpContext context)
    {
        foreach (var name in new[] { SecureName, DevelopmentName })
        {
            context.Response.Cookies.Delete(name, new CookieOptions
            {
                HttpOnly = true,
                Secure = !context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment(),
                SameSite = SameSiteMode.Strict,
                Path = "/"
            });
        }
    }

    internal static bool HasTrustedOrigin(HttpRequest request, IConfiguration configuration)
    {
        if (!request.Headers.TryGetValue("Origin", out StringValues originValues)
            || originValues.Count != 1)
        {
            return false;
        }

        var origin = originValues[0]?.TrimEnd('/');
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["https://app.mrsoftware.dk", "http://localhost:5270", "http://127.0.0.1:5270"];

        return allowedOrigins.Any(allowed =>
            string.Equals(allowed.TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase));
    }
}
