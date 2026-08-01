using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Workslip.Application.Auth;
using Workslip.Application.Common;
using Workslip.Application.Users;
using Workslip.Domain;

namespace Workslip.Api.Helpers;

public sealed class UserClaimsTransformation(
    IUserRepository users,
    IMemoryCache cache,
    ICacheDiagnostics cacheDiagnostics,
    ILogger<UserClaimsTransformation> logger) : IClaimsTransformation
{
    private const string WorkslipUserIdClaim = "workslipUserId";
    private const string OrganizationIdClaim = "organizationId";
    private const string RolesClaim = "roles";
    private const string EntraObjectIdClaim = "oid";
    private const string EntraObjectIdSchemaClaim = "http://schemas.microsoft.com/identity/claims/objectidentifier";
    private const string GuestUserMarker = "#ext#@";
    private static readonly string[] EmailClaimTypes = [ClaimTypes.Email, "email", "preferred_username", ClaimTypes.Upn, "upn", "unique_name"];
    private static readonly MemoryCacheEntryOptions UserCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
    };

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (!principal.Identity?.IsAuthenticated ?? false)
        {
            return principal;
        }

        if (HasClaim(principal, OrganizationIdClaim) && (HasClaim(principal, WorkslipUserIdClaim) || HasClaim(principal, ClaimTypes.Role)))
        {
            return principal;
        }

        if (principal.Identity is not ClaimsIdentity identity)
        {
            return principal;
        }

        var entraId = FirstClaimValue(principal, EntraObjectIdClaim, EntraObjectIdSchemaClaim);
        var emailCandidates = GetEmailCandidates(principal);

        if (string.IsNullOrWhiteSpace(entraId) && emailCandidates.Count == 0)
        {
            return principal;
        }

        var cacheKeys = BuildCacheKeys(entraId, emailCandidates);
        CachedWorkslipUser? user;

        if (TryGetCachedUser(cacheKeys, out user))
        {
            cacheDiagnostics.RecordHit(CacheRegionNames.AuthenticatedUsers);
        }
        else
        {
            cacheDiagnostics.RecordMiss(CacheRegionNames.AuthenticatedUsers);
            var startedAt = Stopwatch.GetTimestamp();

            try
            {
                var row = await users.GetByExternalIdentityAsync(entraId, emailCandidates, CancellationToken.None);
                if (row is null)
                {
                    logger.LogWarning(
                        "Authenticated Entra user was not found in Workslip database. EntraIdPresent={EntraIdPresent} EmailCandidateCount={EmailCandidateCount}.",
                        !string.IsNullOrWhiteSpace(entraId),
                        emailCandidates.Count);
                    return principal;
                }

                user = new CachedWorkslipUser(row.Id, row.OrganizationId, NormalizeRole(row.Role));
                CacheUser(cacheKeys, user);
                cacheDiagnostics.RecordSet(CacheRegionNames.AuthenticatedUsers);
            }
            catch
            {
                cacheDiagnostics.RecordFailure(CacheRegionNames.AuthenticatedUsers);
                throw;
            }
            finally
            {
                cacheDiagnostics.RecordLoad(
                    CacheRegionNames.AuthenticatedUsers,
                    Stopwatch.GetElapsedTime(startedAt));
            }
        }

        if (user is null)
        {
            return principal;
        }

        ReplaceWorkslipClaims(identity, user);
        return principal;
    }

    private bool TryGetCachedUser(IReadOnlyList<string> cacheKeys, out CachedWorkslipUser? user)
    {
        foreach (var cacheKey in cacheKeys)
        {
            if (cache.TryGetValue(cacheKey, out user))
            {
                return true;
            }
        }

        user = null;
        return false;
    }

    private void CacheUser(IReadOnlyList<string> cacheKeys, CachedWorkslipUser user)
    {
        foreach (var cacheKey in cacheKeys)
        {
            cache.Set(cacheKey, user, UserCacheOptions);
        }
    }

    private static bool HasClaim(ClaimsPrincipal principal, string type) =>
        principal.Claims.Any(claim => claim.Type == type && !string.IsNullOrWhiteSpace(claim.Value));

    private static string? FirstClaimValue(ClaimsPrincipal principal, params string[] claimTypes) =>
        claimTypes
            .Select(type => principal.FindFirst(type)?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static IReadOnlyList<string> GetEmailCandidates(ClaimsPrincipal principal) =>
        EmailClaimTypes
            .SelectMany(type => principal.FindAll(type).Select(claim => claim.Value))
            .SelectMany(ExpandEmailCandidate)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IEnumerable<string> ExpandEmailCandidate(string? rawValue)
    {
        var normalized = NormalizeValue(rawValue, lowercase: true);
        if (normalized is null)
        {
            yield break;
        }

        yield return normalized;

        var guestEmail = TryExtractGuestEmail(normalized);
        if (!string.IsNullOrWhiteSpace(guestEmail) && !string.Equals(guestEmail, normalized, StringComparison.OrdinalIgnoreCase))
        {
            yield return guestEmail;
        }
    }

    private static IReadOnlyList<string> BuildCacheKeys(string? entraId, IReadOnlyList<string> emailCandidates)
    {
        var cacheKeys = new List<string>();
        var normalizedEntraId = NormalizeValue(entraId, lowercase: true);
        if (normalizedEntraId is not null)
        {
            cacheKeys.Add($"auth:user:entra:{normalizedEntraId}");
        }

        cacheKeys.AddRange(emailCandidates.Select(email => $"auth:user:email:{email}"));
        return cacheKeys;
    }

    private static string? TryExtractGuestEmail(string normalizedValue)
    {
        var markerIndex = normalizedValue.IndexOf(GuestUserMarker, StringComparison.Ordinal);
        if (markerIndex <= 0)
        {
            return null;
        }

        var alias = normalizedValue[..markerIndex];
        var separatorIndex = alias.LastIndexOf('_');
        if (separatorIndex <= 0 || separatorIndex == alias.Length - 1)
        {
            return null;
        }

        var localPart = alias[..separatorIndex];
        var domainPart = alias[(separatorIndex + 1)..];
        if (!domainPart.Contains('.'))
        {
            return null;
        }

        return $"{localPart}@{domainPart}";
    }

    private static string NormalizeRole(string role)
    {
        var normalized = NormalizeValue(role, lowercase: false);
        return normalized?.ToLowerInvariant() switch
        {
            "superadmin" => Roles.Superadmin,
            "admin" => Roles.Admin,
            "user" => Roles.User,
            "auditor" => Roles.Auditor,
            _ => normalized ?? role
        };
    }

    private static string? NormalizeValue(string? value, bool lowercase) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : lowercase
                ? value.Trim().ToLowerInvariant()
                : value.Trim();

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
