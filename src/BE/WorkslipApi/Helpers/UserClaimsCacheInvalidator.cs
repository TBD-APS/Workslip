using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Workslip.Application.Auth;
using Workslip.Application.Common;
using Workslip.Infrastructure.Diagnostics;

namespace Workslip.Api.Helpers;

/// <summary>
/// Drops this process's cached claims for a single user after their role,
/// organization or account changed, so the operator who made the change sees it take
/// effect on the request they are looking at instead of waiting out
/// <see cref="UserClaimsCache.Lifetime"/>.
/// </summary>
/// <remarks>
/// <para>
/// That is the whole job. Cached claims are process-local by construction (see
/// <see cref="UserClaimsCache"/>), so there is no shared copy to delete, no
/// invalidation to publish and no ordering to defend - which is why this type no
/// longer stamps anything, no longer blocks on a cache round trip and no longer has
/// a timeout.
/// </para>
/// <para>
/// <b>What this does NOT give:</b> immediate cross-replica revocation. The other
/// replicas keep their own copies until those lapse, so a revoked role can still be
/// honoured elsewhere for up to <see cref="UserClaimsCache.Lifetime"/>. Making that
/// immediate needs a backplane (Redis pub/sub telling every replica to drop its L1)
/// or a shorter access-token lifetime; it cannot be bought with a cache tier, and
/// trying to buy it with one is what this design deliberately walked back from.
/// </para>
/// <para>
/// <b>Why <see cref="IMemoryCache"/> rather than <c>HybridCache.RemoveAsync</c>.</b>
/// <c>DefaultHybridCache.RemoveAsync</c> ignores entry flags - verified against the
/// installed Microsoft.Extensions.Caching.Hybrid 10.6.0 assembly, where it is
/// exactly <c>_localCache.Remove(key)</c> followed by
/// <c>_backendCache.RemoveAsync(key)</c>. On a Redis deployment it would therefore
/// send a DEL for a key that is never written, putting an e-mail-address-shaped key
/// name on the Redis wire to accomplish nothing. Removing from L1 directly is the
/// same removal without that. The coupling it accepts is that HybridCache's L1 *is*
/// the container's <see cref="IMemoryCache"/>, keyed by the raw cache key
/// (<c>_localCache = GetRequiredService&lt;IMemoryCache&gt;(services)</c> and
/// <c>_localCache.CreateEntry((object)key)</c> in <c>SetL1</c>, same version); a
/// package upgrade that changed either would make this a silent no-op, so
/// <c>UserClaimsTransformationRoleRefreshTests</c> writes through the real
/// <see cref="HybridCache"/> with the production options and asserts that an
/// invalidation is observable, which fails loudly if that ever changes.
/// </para>
/// </remarks>
public sealed class UserClaimsCacheInvalidator(
    IMemoryCache localClaimsCache,
    ICacheDiagnostics cacheDiagnostics,
    ILogger<UserClaimsCacheInvalidator> logger) : IUserClaimsCacheInvalidator
{
    public void Invalidate(string? entraId, string? email, string? entraEmail)
    {
        var cacheKeys = BuildCacheKeys(entraId, email, entraEmail);
        if (cacheKeys.Count == 0)
        {
            return;
        }

        try
        {
            foreach (var cacheKey in cacheKeys)
            {
                localClaimsCache.Remove(cacheKey);
            }

            cacheDiagnostics.RecordInvalidation(CacheRegionNames.AuthenticatedUsers);
        }
        catch (Exception exception)
        {
            // In-process dictionary removals, so this is not an expected path - a
            // disposed cache during host shutdown is about the only way in. It is
            // still caught, because the database write has already committed and
            // throwing here would report a failure for a change that did take
            // effect. Degrading costs the operator the wait the lifetime already
            // bounds.
            cacheDiagnostics.RecordFailure(CacheRegionNames.AuthenticatedUsers);

            // The category, not the exception. These keys carry the user's e-mail
            // address, so an exception that quotes the key it failed on would put that
            // address in every sink - and the rule in
            // Docs/operations/CACHE_DIAGNOSTICS.md is that no logger on a cache path
            // logs the exception object.
            logger.LogError(
                "Dropping cached Workslip user claims failed. The change is committed; cached claims for this user may be served for up to {StaleSeconds} seconds. Cache failure: {CacheFailure}.",
                (int)UserClaimsCache.Lifetime.TotalSeconds,
                DistributedCacheProbe.DescribeFailureForLog(exception));
        }
    }

    private static IReadOnlyList<string> BuildCacheKeys(string? entraId, string? email, string? entraEmail)
    {
        var cacheKeys = new List<string>(3);

        var normalizedEntraId = UserClaimsCache.Normalize(entraId);
        if (normalizedEntraId is not null)
        {
            cacheKeys.Add(UserClaimsCache.EntraKey(normalizedEntraId));
        }

        AddEmailKey(cacheKeys, email);
        AddEmailKey(cacheKeys, entraEmail);
        return cacheKeys;
    }

    private static void AddEmailKey(List<string> cacheKeys, string? email)
    {
        var normalized = UserClaimsCache.Normalize(email);
        if (normalized is null)
        {
            return;
        }

        // email and entraEmail are usually the same address; one removal is enough.
        var cacheKey = UserClaimsCache.EmailKey(normalized);
        if (!cacheKeys.Contains(cacheKey, StringComparer.Ordinal))
        {
            cacheKeys.Add(cacheKey);
        }
    }
}
