using System.Security.Cryptography;
using System.Text;
using Ardalis.Result;
using Microsoft.Extensions.Logging;
using Workslip.Domain.Models;

namespace Workslip.Application.Auth;

public sealed class RefreshSessionService(
    IRefreshSessionRepository sessions,
    TimeProvider timeProvider,
    RefreshSessionPolicy policy,
    ILogger<RefreshSessionService> logger) : IRefreshSessionService
{
    public async Task<RefreshSessionGrant> StartAsync(
        AuthUserInfo user,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var refreshToken = GenerateRefreshToken();
        var session = BuildSession(
            user,
            familyId: Guid.NewGuid(),
            refreshToken,
            now,
            now.Add(policy.AbsoluteLifetime));

        await sessions.CreateAsync(session, cancellationToken);
        return new RefreshSessionGrant(user, refreshToken, CookieExpiry(now, session.ExpiresAt));
    }

    public async Task<Result<RefreshSessionGrant>> RefreshAsync(
        string? refreshToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            logger.LogInformation("Refresh rejected because the request carried no refresh cookie.");
            return Result<RefreshSessionGrant>.Unauthorized();
        }

        var tokenHash = HashToken(refreshToken);
        var now = timeProvider.GetUtcNow();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var current = await sessions.GetByTokenHashAsync(tokenHash, cancellationToken);
            if (current is null)
            {
                logger.LogInformation("Refresh rejected because the refresh token is unknown or was already cleaned up.");
                return Result<RefreshSessionGrant>.Unauthorized();
            }

            if (current.RevokedAt is not null)
            {
                // A revoked token comes back either after the family was already revoked
                // (a no-op below) or after it was superseded by lost-response recovery. In
                // the second case two holders exist, so the whole family is revoked.
                await sessions.RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
                logger.LogInformation("Refresh rejected because the refresh token was revoked; the session family is revoked.");
                return Result<RefreshSessionGrant>.Unauthorized();
            }

            if (current.ExpiresAt <= now)
            {
                await sessions.RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
                logger.LogInformation("Refresh session rejected because its absolute lifetime expired.");
                return Result<RefreshSessionGrant>.Unauthorized();
            }

            if (current.CreatedAt.Add(policy.IdleLifetime) <= now)
            {
                await sessions.RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
                logger.LogInformation("Refresh session rejected because it was idle longer than the idle lifetime.");
                return Result<RefreshSessionGrant>.Unauthorized();
            }

            if (current.UsedAt is not null)
            {
                var insideGrace = now - current.UsedAt.Value <= policy.ConcurrentRefreshGrace;
                if (insideGrace)
                {
                    if (current.GraceReuseAt is null)
                    {
                        await sessions.TryClaimConcurrentRefreshGraceAsync(
                            current.Id,
                            current.ConcurrencyStamp,
                            now,
                            cancellationToken);
                    }
                    logger.LogInformation("Concurrent refresh race recovered inside the bounded grace window.");
                    return Result<RefreshSessionGrant>.Conflict("refresh_race");
                }

                // Outside the grace window the browser either never received the response
                // that carried the replacement (a sleeping laptop or a lost mobile
                // connection), or a second holder is replaying the token. Only the first
                // case leaves the direct replacement unused, so only that case is recovered.
                var successor = await sessions.GetUnusedSuccessorAsync(current, cancellationToken);
                if (successor is null)
                {
                    await sessions.RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
                    logger.LogWarning("Refresh-token reuse detected; the session family was revoked.");
                    return Result<RefreshSessionGrant>.Unauthorized();
                }

                var recoveredUser = await GetEligibleUserOrRevokeAsync(current, now, cancellationToken);
                if (recoveredUser is null)
                {
                    return Result<RefreshSessionGrant>.Unauthorized();
                }

                var recoveredToken = GenerateRefreshToken();
                var recovered = BuildSession(recoveredUser, current.FamilyId, recoveredToken, now, current.ExpiresAt);
                if (await sessions.TryReplaceUnusedSuccessorAsync(current, successor, recovered, now, cancellationToken))
                {
                    logger.LogInformation("Refresh response was lost; the unused replacement was superseded and the session continues.");
                    return Result<RefreshSessionGrant>.Success(
                        new RefreshSessionGrant(recoveredUser, recoveredToken, CookieExpiry(now, recovered.ExpiresAt)));
                }

                // A competing request changed the token or its replacement; reload once
                // and classify it again.
                continue;
            }

            var user = await GetEligibleUserOrRevokeAsync(current, now, cancellationToken);
            if (user is null)
            {
                return Result<RefreshSessionGrant>.Unauthorized();
            }

            var replacementToken = GenerateRefreshToken();
            var replacement = BuildSession(
                user,
                current.FamilyId,
                replacementToken,
                now,
                current.ExpiresAt);

            if (await sessions.TryRotateAsync(current, replacement, now, cancellationToken))
            {
                return Result<RefreshSessionGrant>.Success(
                    new RefreshSessionGrant(user, replacementToken, CookieExpiry(now, replacement.ExpiresAt)));
            }

            // A competing request may have rotated this token between the read
            // and the atomic compare/update. Reload once and classify it through
            // the same bounded grace/reuse rules above.
        }

        return Result<RefreshSessionGrant>.Conflict("refresh_race");
    }

    public async Task RevokeAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var session = await sessions.GetByTokenHashAsync(HashToken(refreshToken), cancellationToken);
        if (session is not null)
        {
            await sessions.RevokeFamilyAsync(
                session.FamilyId,
                timeProvider.GetUtcNow(),
                cancellationToken);
        }
    }

    private async Task<AuthUserInfo?> GetEligibleUserOrRevokeAsync(
        RefreshSessionRow current,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var user = await sessions.GetEligibleUserAsync(
            current.UserId,
            current.OrganizationId,
            cancellationToken);
        if (user is null)
        {
            await sessions.RevokeFamilyAsync(current.FamilyId, now, cancellationToken);
            logger.LogInformation("Refresh session rejected because the user or organization is no longer active.");
        }

        return user;
    }

    // The cookie lives until the idle limit, but never beyond the family's absolute
    // expiry. Each successful refresh moves the idle limit forward.
    private DateTimeOffset CookieExpiry(DateTimeOffset now, DateTimeOffset absoluteExpiry)
    {
        var idleExpiry = now.Add(policy.IdleLifetime);
        return idleExpiry < absoluteExpiry ? idleExpiry : absoluteExpiry;
    }

    internal static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static RefreshSessionRow BuildSession(
        AuthUserInfo user,
        Guid familyId,
        string refreshToken,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt) => new()
        {
            Id = Guid.NewGuid(),
            FamilyId = familyId,
            UserId = user.UserId,
            OrganizationId = user.OrganizationId,
            TokenHash = HashToken(refreshToken),
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
            ConcurrencyStamp = Guid.NewGuid()
        };
}
