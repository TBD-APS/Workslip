using Microsoft.Extensions.Caching.Memory;
using Workslip.Application.Auth;

namespace Workslip.Api.Helpers;

public sealed class UserClaimsCacheInvalidator(IMemoryCache cache) : IUserClaimsCacheInvalidator
{
    public void Invalidate(string? entraId, string? email, string? entraEmail)
    {
        RemoveEntraKey(entraId);
        RemoveEmailKey(email);
        RemoveEmailKey(entraEmail);
    }

    private void RemoveEntraKey(string? entraId)
    {
        var normalized = Normalize(entraId);
        if (normalized is not null)
        {
            cache.Remove($"auth:user:entra:{normalized}");
        }
    }

    private void RemoveEmailKey(string? email)
    {
        var normalized = Normalize(email);
        if (normalized is not null)
        {
            cache.Remove($"auth:user:email:{normalized}");
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
