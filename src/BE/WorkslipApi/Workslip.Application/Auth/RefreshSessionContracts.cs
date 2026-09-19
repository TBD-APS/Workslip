using Workslip.Domain.Models;

namespace Workslip.Application.Auth;

public sealed record RefreshSessionPolicy(
    TimeSpan AbsoluteLifetime,
    TimeSpan ConcurrentRefreshGrace,
    TimeSpan CleanupRetention);

public sealed record RefreshSessionGrant(
    AuthUserInfo User,
    string RefreshToken,
    DateTimeOffset ExpiresAt);

public interface IRefreshSessionService
{
    Task<RefreshSessionGrant> StartAsync(AuthUserInfo user, CancellationToken cancellationToken);
    Task<Ardalis.Result.Result<RefreshSessionGrant>> RefreshAsync(string? refreshToken, CancellationToken cancellationToken);
    Task RevokeAsync(string? refreshToken, CancellationToken cancellationToken);
}

public interface IRefreshSessionRepository
{
    Task CreateAsync(RefreshSessionRow session, CancellationToken cancellationToken);
    Task<RefreshSessionRow?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);
    Task<AuthUserInfo?> GetEligibleUserAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken);
    Task<bool> TryRotateAsync(
        RefreshSessionRow current,
        RefreshSessionRow replacement,
        DateTimeOffset usedAt,
        CancellationToken cancellationToken);
    Task<bool> TryClaimConcurrentRefreshGraceAsync(
        Guid sessionId,
        Guid expectedConcurrencyStamp,
        DateTimeOffset graceReuseAt,
        CancellationToken cancellationToken);
    Task RevokeFamilyAsync(Guid familyId, DateTimeOffset revokedAt, CancellationToken cancellationToken);
    Task<int> DeleteExpiredFamiliesAsync(DateTimeOffset deleteBefore, CancellationToken cancellationToken);
}
