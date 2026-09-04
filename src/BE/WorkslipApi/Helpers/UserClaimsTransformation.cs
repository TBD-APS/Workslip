using System.ComponentModel;
using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Hybrid;
using Workslip.Application.Auth;
using Workslip.Application.Common;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Infrastructure.Diagnostics;

namespace Workslip.Api.Helpers;

public sealed class UserClaimsTransformation(
    IUserRepository users,
    HybridCache cache,
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

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (!principal.Identity?.IsAuthenticated ?? false)
        {
            return principal;
        }

        // Delegated Superadmin organization sessions intentionally carry an
        // effective tenant OrganizationId that differs from the platform user's
        // persisted OrganizationId. Never replace those scoped claims here.
        if (HasClaim(principal, JwtHelper.DelegatedOrganizationSessionClaim))
        {
            return principal;
        }

        // A principal already transformed during this authentication pass is
        // complete. Ordinary local JWTs do not contain workslipUserId, so they
        // continue below and have role/organization refreshed from Workslip DB.
        if (HasClaim(principal, OrganizationIdClaim)
            && HasClaim(principal, WorkslipUserIdClaim)
            && HasClaim(principal, ClaimTypes.Role))
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
        var user = await TryGetCachedUserAsync(cacheKeys);

        if (user is not null)
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
                        "Authenticated user was not found in Workslip database. EntraIdPresent={EntraIdPresent} EmailCandidateCount={EmailCandidateCount}.",
                        !string.IsNullOrWhiteSpace(entraId),
                        emailCandidates.Count);
                    RemoveWorkslipClaims(identity);
                    return principal;
                }

                user = new CachedWorkslipUser(row.Id, row.OrganizationId, NormalizeRole(row.Role));
                if (await TryCacheUserAsync(cacheKeys, user))
                {
                    cacheDiagnostics.RecordSet(CacheRegionNames.AuthenticatedUsers);
                }
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

    /// <summary>
    /// Probes the candidate keys in order and returns the first cached user, or
    /// <see langword="null"/> when nothing usable is cached. A cache that is
    /// unreachable or faulted must never fail authentication: every failure here
    /// degrades to a miss so the caller resolves the user from the database.
    /// </summary>
    private async ValueTask<CachedWorkslipUser?> TryGetCachedUserAsync(IReadOnlyList<string> cacheKeys)
    {
        foreach (var cacheKey in cacheKeys)
        {
            CachedWorkslipUser? cached;

            try
            {
                // HybridCache has no "try get", so this is GetOrCreateAsync with the
                // factory suppressed: DisableUnderlyingData makes the call return
                // default(T) instead of invoking the callback, and writes nothing -
                // SetDefaultResult completes the waiting callers without calling SetL1.
                // The tags are still passed because they are what the administrative
                // cache-clear checks an entry against on every read.
                cached = await cache.GetOrCreateAsync<object?, CachedWorkslipUser?>(
                    cacheKey,
                    null,
                    static (_, _) => new ValueTask<CachedWorkslipUser?>((CachedWorkslipUser?)null),
                    UserClaimsCache.ProbeOptions,
                    UserClaimsCache.Tags,
                    CancellationToken.None);
            }
            catch (Exception exception)
            {
                cacheDiagnostics.RecordFailure(CacheRegionNames.AuthenticatedUsers);

                // The classified category, never the exception object. This key
                // contains the user's e-mail address, and a provider exception quotes
                // the key of the operation that failed alongside the cache endpoint -
                // so passing the exception to the logger is how an e-mail address and a
                // cache address reach every sink. Cached claims never touch the shared
                // tier (UserClaimsCache.ProbeOptions), which makes this the defensive
                // half of the rule rather than the reachable one; the rule is the same
                // either way, and the flags are one call-site edit away from changing.
                logger.LogWarning(
                    "Reading cached Workslip user claims failed; resolving the user from the database instead. Cache failure: {CacheFailure}.",
                    DistributedCacheProbe.DescribeFailureForLog(exception));
                return null;
            }

            if (cached is not null)
            {
                return cached;
            }
        }

        return null;
    }

    /// <summary>
    /// Caches the resolved user under every candidate key, in this process only, and
    /// reports whether anything was actually cached. A cache write that fails must not
    /// fail the request that just resolved the user successfully: it costs a repeated
    /// database read on the next request and nothing else.
    /// </summary>
    private async ValueTask<bool> TryCacheUserAsync(IReadOnlyList<string> cacheKeys, CachedWorkslipUser user)
    {
        var cached = false;

        foreach (var cacheKey in cacheKeys)
        {
            try
            {
                await cache.SetAsync(
                    cacheKey,
                    user,
                    UserClaimsCache.EntryOptions,
                    UserClaimsCache.Tags,
                    CancellationToken.None);
                cached = true;
            }
            catch (Exception exception)
            {
                cacheDiagnostics.RecordFailure(CacheRegionNames.AuthenticatedUsers);

                // Same rule as the read path: the category, not the exception that
                // would quote this e-mail-address-shaped key back into the log.
                logger.LogWarning(
                    "Caching Workslip user claims failed. The claims stay correct; the next request repeats the database lookup. Cache failure: {CacheFailure}.",
                    DistributedCacheProbe.DescribeFailureForLog(exception));
                break;
            }
        }

        return cached;
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
        var normalized = UserClaimsCache.Normalize(rawValue);
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
        var normalizedEntraId = UserClaimsCache.Normalize(entraId);
        if (normalizedEntraId is not null)
        {
            cacheKeys.Add(UserClaimsCache.EntraKey(normalizedEntraId));
        }

        cacheKeys.AddRange(emailCandidates.Select(UserClaimsCache.EmailKey));
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
        var normalized = string.IsNullOrWhiteSpace(role) ? null : role.Trim();
        return normalized?.ToLowerInvariant() switch
        {
            "superadmin" => Roles.Superadmin,
            "admin" => Roles.Admin,
            "user" => Roles.User,
            "auditor" => Roles.Auditor,
            _ => normalized ?? role
        };
    }

    private static void ReplaceWorkslipClaims(ClaimsIdentity identity, CachedWorkslipUser user)
    {
        RemoveWorkslipClaims(identity);

        identity.AddClaim(new Claim(WorkslipUserIdClaim, user.UserId.ToString()));
        identity.AddClaim(new Claim(OrganizationIdClaim, user.OrganizationId.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Role, user.Role));
    }

    private static void RemoveWorkslipClaims(ClaimsIdentity identity)
    {
        foreach (var claim in identity.Claims.Where(IsWorkslipManagedClaim).ToArray())
        {
            identity.RemoveClaim(claim);
        }
    }

    private static bool IsWorkslipManagedClaim(Claim claim) =>
        claim.Type == WorkslipUserIdClaim
        || claim.Type == OrganizationIdClaim
        || claim.Type == ClaimTypes.Role
        || claim.Type == RolesClaim;
}

/// <summary>
/// The shared shape of the authenticated-user cache: key layout, tags, lifetime and
/// the entry options that keep it inside one process.
/// <see cref="UserClaimsTransformation"/> fills it and
/// <see cref="UserClaimsCacheInvalidator"/> drops from it, so both derive keys here
/// rather than repeating the string layout.
/// </summary>
internal static class UserClaimsCache
{
    // Key shape is unchanged from the IMemoryCache implementation this replaced:
    // "auth:user:entra:{oid}" and "auth:user:email:{email}", both lowercased. The
    // e-mail address in the key never leaves this process - see EntryOptions.
    private const string EntraKeyPrefix = "auth:user:entra:";
    private const string EmailKeyPrefix = "auth:user:email:";

    // ---------------------------------------------------------------------------
    // Claims are cached PER PROCESS ONLY.
    //
    // Redis is registered as HybridCache's optional L2 for the caches whose
    // staleness is benign - reference data, job lists. A user's id, organization and
    // role are not among them, and they are neither written to nor read from that
    // shared tier: see EntryOptions for the two flags that enforce it and how they
    // were verified.
    //
    // Why not, given that a shared entry would save a database read per replica per
    // lifetime: because HybridCache has no backplane, and without one a shared
    // authorization entry cannot be invalidated safely. Verified against the
    // installed Microsoft.Extensions.Caching.Hybrid 10.6.0 assembly, which is the
    // version the whole app loads (the 10.1.0 pins in Workslip.Application and
    // Workslip.Infrastructure are floors NuGet unifies upwards):
    //
    //   * DefaultHybridCache.RemoveAsync(key) is "_localCache.Remove(key)" plus
    //     "_backendCache.RemoveAsync(key)". It drops the shared row and the caller's
    //     own L1 entry. There is no notification: the L1 copies held by the other
    //     replicas are untouched.
    //   * RemoveByTagAsync(tag) stamps a timestamp into the L2 key
    //     "__MSFT_HCT__" + tag. A replica reads a given tag's timestamp from L2 at
    //     most ONCE - IsTagExpired and PrefetchTags both TryAdd into
    //     _tagInvalidationTimes on first use, and nothing refreshes or evicts that
    //     dictionary - so a replica that has already touched a tag never observes a
    //     later invalidation of it made elsewhere.
    //
    // So neither a key removal nor a tag invalidation evicts another replica's L1,
    // and a shared row therefore has to be protected at WRITE time instead: an
    // invalidation must be able to withhold a value that an in-flight resolution
    // read before it. Building that needs a token that every replica agrees on, and
    // the only store available for it is the same L2 the token exists to protect -
    // so when the L2 cannot answer, the check either fails open (a revoked role
    // pinned deployment-wide for the shared row's whole lifetime, with the
    // invalidation logged as a success) or fails closed (a Redis blip turns every
    // authenticated request into a database read). Two verification rounds
    // reproduced the first of those against a real Redis. The value bought was one
    // saved user-row read per replica per lifetime; a short local lifetime produces
    // the same propagation bound on its own, so the shared half was paying the whole
    // correctness cost for nothing.
    //
    // WHAT THIS DESIGN DOES NOT GIVE: there is no immediate cross-replica
    // revocation. A replica that did not serve the change keeps answering the old
    // role until its own copy expires. If immediate revocation is ever required, it
    // needs a backplane - Redis pub/sub telling every replica to drop its L1 - or a
    // shorter access-token lifetime. It does not need another cache tier, and no
    // cache configuration can provide it.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// How long one process keeps a resolved user, and therefore the whole
    /// propagation bound for a role, organization or account change.
    /// </summary>
    /// <remarks>
    /// One minute, chosen against the only two quantities that move with it.
    /// Shortening it narrows the window in which a replica that did not serve a
    /// change still honours the old role; lengthening it saves database reads. What
    /// is being traded away is one
    /// <see cref="IUserRepository.GetByExternalIdentityAsync"/> per authenticated
    /// user per replica per lifetime, and that read is not a keyed seek:
    /// <c>Users</c> is indexed only on <c>Id</c> and
    /// <c>(OrganizationId, Id)</c>, and the predicate matches <c>EntraId</c> OR
    /// <c>TRIM(LOWER(Email))</c> / <c>TRIM(LOWER(EntraEmail))</c>, which is not
    /// sargable - so every miss scans the user table. It is still the cheaper side of
    /// the trade at this table size: one scan of a few hundred narrow rows, at a
    /// minute, is under two per second even with a hundred users active on each of
    /// three replicas. The number to watch if that stops being true is the row count
    /// of <c>Users</c>, not the request rate, and the fix would be an index that
    /// makes the lookup sargable rather than a longer lifetime.
    ///
    /// Sixty seconds is also short enough that an operator who changes a role and
    /// refreshes sees the change, and it is the bound the withdrawn shared-tier design
    /// was measured to deliver - so dropping the shared tier costs no propagation
    /// guarantee. The hour this replaces is the defect: it was long enough for a
    /// revoked Superadmin to keep working through a whole support call.
    /// </remarks>
    internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);

    // CacheTagNames.All keeps cached claims part of POST /api/admin/cache/clear: the
    // clearing process invalidates the tag locally and DefaultHybridCache.IsValid
    // re-checks every L1 hit against the tag timestamps, so a tagged entry is
    // rejected from the moment of the clear - the same reach the IMemoryCache
    // implementation gave these entries, i.e. the replica that serves the clear.
    // Deliberately no per-user tag: it would not reach another replica either, and it
    // would grow HybridCache's tag dictionary once per user, forever.
    internal static readonly string[] Tags = [CacheTagNames.All, CacheRegionNames.AuthenticatedUsers];

    /// <summary>
    /// Write options: one minute, in this process and nowhere else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="HybridCacheEntryFlags.DisableDistributedCache"/> is
    /// <c>DisableDistributedCacheRead | DisableDistributedCacheWrite</c> (4 | 8), and
    /// both halves carry weight rather than one being belt to the other's braces.
    /// Read out of the installed 10.6.0 assembly rather than assumed from the names:
    /// </para>
    /// <list type="bullet">
    /// <item><c>DisableDistributedCacheWrite</c> is what keeps the payload - user id,
    /// organization, role - out of Redis. DefaultHybridCache guards its only L2 write
    /// with <c>if ((activeFlags &amp; 8) == 0) await SetL2Async(...)</c>, and
    /// <c>SetAsync</c> reaches that same code path (it ORs in 5, disabling both reads,
    /// and leaves the write bits alone).</item>
    /// <item><c>DisableDistributedCacheRead</c> is what keeps the key - which contains
    /// the user's e-mail address - out of Redis commands: the backend read and its
    /// <c>PrefetchTags</c> call sit behind <c>if ((activeFlags &amp; 4) == 0)</c>. It
    /// also means this process can never trust a claims row an older revision left in
    /// the shared tier.</item>
    /// <item>No <c>DisableLocalCache*</c> bit is set, so the L1 write stays enabled:
    /// <c>SetL1</c> stores the entry in the container's <c>IMemoryCache</c> under the
    /// unmodified key with <c>AbsoluteExpirationRelativeToNow</c> = the effective local
    /// expiration.</item>
    /// </list>
    /// <para>
    /// Both expirations are set to the same minute on purpose. <c>Expiration</c> is the
    /// L2 lifetime, which nothing here uses, but HybridCache also applies it as the
    /// ceiling on the local one (<c>GetEffectiveLocalCacheExpiration</c> returns the
    /// smaller of the two), so leaving it at the five-minute package default would say
    /// one thing and mean another to the next reader.
    /// </para>
    /// </remarks>
    internal static readonly HybridCacheEntryOptions EntryOptions = new()
    {
        Expiration = Lifetime,
        LocalCacheExpiration = Lifetime,
        Flags = HybridCacheEntryFlags.DisableDistributedCache
    };

    /// <summary>
    /// Read-only probe options: the same process-local entry, plus
    /// <see cref="HybridCacheEntryFlags.DisableUnderlyingData"/> so a miss returns
    /// <c>default(T)</c> instead of invoking the factory. The lifetimes are repeated
    /// because HybridCache reads them from whichever options the call passes.
    /// </summary>
    internal static readonly HybridCacheEntryOptions ProbeOptions = new()
    {
        Expiration = Lifetime,
        LocalCacheExpiration = Lifetime,
        Flags = HybridCacheEntryFlags.DisableDistributedCache | HybridCacheEntryFlags.DisableUnderlyingData
    };

    internal static string EntraKey(string normalizedEntraId) => EntraKeyPrefix + normalizedEntraId;

    internal static string EmailKey(string normalizedEmail) => EmailKeyPrefix + normalizedEmail;

    internal static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}

// Immutable: HybridCache only skips the per-read serialize/deserialize round trip
// for sealed types marked this way (ImmutableTypeCache.IsTypeImmutable), which keeps
// L1 hits on the authentication path allocation-free.
[ImmutableObject(true)]
internal sealed record CachedWorkslipUser(Guid UserId, Guid OrganizationId, string Role);
