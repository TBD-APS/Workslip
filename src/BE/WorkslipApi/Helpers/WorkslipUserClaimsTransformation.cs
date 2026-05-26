using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Workslip.Application.Users;

namespace Workslip.Api.Helpers;

public sealed class WorkslipUserClaimsTransformation(
    IUserRepository users,
    IMemoryCache cache,
    ILogger<WorkslipUserClaimsTransformation> logger) : IClaimsTransformation
{
    private const string WorkslipUserIdClaim = "workslipUserId";
    private const string OrganizationIdClaim = "organizationId";
    private const string RolesClaim = "roles";
    private const string EntraObjectIdClaim = "oid";
    private const string EntraObjectIdSchemaClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private static readonly MemoryCacheEntryOptions UserCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
    };

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
        {
            return principal;
        }

        if (HasClaim(principal, OrganizationIdClaim) && (HasClaim(principal, WorkslipUserIdClaim) || HasClaim(principal, ClaimTypes.NameIdentifier)))
        {
            return principal;
        }

        if (principal.Identity is not ClaimsIdentity identity)
        {
            return principal;
        }

        var entraId = FirstClaimValue(principal, EntraObjectIdClaim, EntraObjectIdSchemaClaim);
        var email = FirstClaimValue(principal, ClaimTypes.Email, "email", "preferred_username", ClaimTypes.Upn, "upn");
        if (string.IsNullOrWhiteSpace(entraId) && string.IsNullOrWhiteSpace(email))
        {
            return principal;
        }

        var cacheKey = !string.IsNullOrWhiteSpace(entraId)
            ? $"auth:user:entra:{entraId}"
            : $"auth:user:email:{email!.Trim().ToLowerInvariant()}";

        if (!cache.TryGetValue(cacheKey, out CachedWorkslipUser? user))
        {
            var row = await users.GetByExternalIdentityAsync(entraId, email, CancellationToken.None);
            if (row is null)
            {
                logger.LogWarning("Authenticated Entra user was not found in Workslip database. EntraIdPresent={EntraIdPresent} EmailPresent={EmailPresent}.",
                    !string.IsNullOrWhiteSpace(entraId),
                    !string.IsNullOrWhiteSpace(email));
                return principal;
            }

            user = new CachedWorkslipUser(row.Id, row.OrganizationId, row.Role);
            cache.Set(cacheKey, user, UserCacheOptions);
        }

        if (user is null)
        {
            return principal;
        }

        ReplaceWorkslipClaims(identity, user);
        return principal;
    }

    private static bool HasClaim(ClaimsPrincipal principal, string type) =>
        principal.Claims.Any(claim => claim.Type == type && !string.IsNullOrWhiteSpace(claim.Value));

    private static string? FirstClaimValue(ClaimsPrincipal principal, params string[] claimTypes) =>
        claimTypes
            .Select(type => principal.FindFirst(type)?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static void ReplaceWorkslipClaims(ClaimsIdentity identity, CachedWorkslipUser user)
    {
        foreach (var claim in identity.Claims.Where(IsWorkslipManagedClaim).ToArray())
        {
            identity.RemoveClaim(claim);
        }

        identity.AddClaim(new Claim(WorkslipUserIdClaim, user.UserId.ToString()));
        identity.AddClaim(new Claim(OrganizationIdClaim, user.OrganizationId.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Role, user.Role));
    }

    private static bool IsWorkslipManagedClaim(Claim claim) =>
        claim.Type == WorkslipUserIdClaim
        || claim.Type == OrganizationIdClaim
        || claim.Type == ClaimTypes.Role
        || claim.Type == RolesClaim;

    private sealed record CachedWorkslipUser(Guid UserId, Guid OrganizationId, string Role);
}
