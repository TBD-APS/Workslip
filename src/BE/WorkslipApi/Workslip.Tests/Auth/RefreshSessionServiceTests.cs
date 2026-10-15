using System.Security.Cryptography;
using System.Text;
using Ardalis.Result;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Auth;

public sealed class RefreshSessionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly AuthUserInfo User = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "user@example.test", "Test User", "User");

    [Fact]
    public async Task StartAsync_PersistsOnlyHash_WithIdleCookieExpiryAndAbsoluteFamilyExpiry()
    {
        var repository = new FakeRepository(User);
        var service = CreateService(repository);

        var grant = await service.StartAsync(User, CancellationToken.None);

        var stored = Assert.Single(repository.Rows);
        Assert.NotEqual(grant.RefreshToken, stored.TokenHash);
        Assert.Equal(64, stored.TokenHash.Length);
        Assert.Equal(Hash(grant.RefreshToken), stored.TokenHash);
        Assert.Equal(Now.AddDays(14), grant.ExpiresAt);
        Assert.Equal(Now.AddDays(90), stored.ExpiresAt);
    }

    [Fact]
    public async Task RefreshAsync_RotatesOneUseToken_AndSlidesTheIdleExpiry()
    {
        var repository = new FakeRepository(User);
        var time = new FixedTimeProvider(Now);
        var service = CreateService(repository, time);
        var original = await service.StartAsync(User, CancellationToken.None);

        time.UtcNow = Now.AddDays(10);
        var result = await service.RefreshAsync(original.RefreshToken, CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.NotEqual(original.RefreshToken, result.Value.RefreshToken);
        Assert.Equal(Now.AddDays(24), result.Value.ExpiresAt);
        Assert.Equal(2, repository.Rows.Count);
        Assert.NotNull(repository.Rows.Single(x => x.TokenHash == Hash(original.RefreshToken)).UsedAt);
        Assert.All(repository.Rows, row => Assert.Equal(Now.AddDays(90), row.ExpiresAt));
    }

    [Fact]
    public async Task RefreshAsync_AfterTheIdleLifetime_RevokesFamily()
    {
        var repository = new FakeRepository(User);
        var time = new FixedTimeProvider(Now);
        var service = CreateService(repository, time);
        var original = await service.StartAsync(User, CancellationToken.None);

        time.UtcNow = Now.AddDays(14).AddMinutes(1);
        var result = await service.RefreshAsync(original.RefreshToken, CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        Assert.NotNull(Assert.Single(repository.Rows).RevokedAt);
    }

    [Fact]
    public async Task RefreshAsync_ActiveUse_StopsAtTheAbsoluteLifetime()
    {
        var repository = new FakeRepository(User);
        var time = new FixedTimeProvider(Now);
        var service = CreateService(repository, time);
        var token = (await service.StartAsync(User, CancellationToken.None)).RefreshToken;

        RefreshSessionGrant? last = null;
        for (var day = 10; day <= 80; day += 10)
        {
            time.UtcNow = Now.AddDays(day);
            last = (await service.RefreshAsync(token, CancellationToken.None)).Value;
            token = last.RefreshToken;
        }

        Assert.Equal(Now.AddDays(90), last!.ExpiresAt);
        time.UtcNow = Now.AddDays(90).AddMinutes(1);
        var expired = await service.RefreshAsync(token, CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, expired.Status);
        Assert.All(repository.Rows, row => Assert.NotNull(row.RevokedAt));
    }

    [Fact]
    public async Task RefreshAsync_ConcurrentReuseGetsGrace_LateReuseAfterReplacementWasUsedRevokesFamily()
    {
        var repository = new FakeRepository(User);
        var time = new FixedTimeProvider(Now);
        var service = CreateService(repository, time);
        var original = await service.StartAsync(User, CancellationToken.None);
        var rotated = await service.RefreshAsync(original.RefreshToken, CancellationToken.None);

        var concurrent = await service.RefreshAsync(original.RefreshToken, CancellationToken.None);
        var anotherConcurrent = await service.RefreshAsync(original.RefreshToken, CancellationToken.None);
        time.UtcNow = Now.AddMinutes(10);
        await service.RefreshAsync(rotated.Value.RefreshToken, CancellationToken.None);
        var reuse = await service.RefreshAsync(original.RefreshToken, CancellationToken.None);

        Assert.Equal(ResultStatus.Conflict, concurrent.Status);
        Assert.Equal(ResultStatus.Conflict, anotherConcurrent.Status);
        Assert.Equal(ResultStatus.Unauthorized, reuse.Status);
        Assert.All(repository.Rows, row => Assert.NotNull(row.RevokedAt));
    }

    [Fact]
    public async Task RefreshAsync_WhenTheReplacementNeverArrived_ContinuesTheSession()
    {
        var repository = new FakeRepository(User);
        var time = new FixedTimeProvider(Now);
        var service = CreateService(repository, time);
        var original = await service.StartAsync(User, CancellationToken.None);
        var lost = await service.RefreshAsync(original.RefreshToken, CancellationToken.None);

        time.UtcNow = Now.AddHours(4);
        var recovered = await service.RefreshAsync(original.RefreshToken, CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, recovered.Status);
        Assert.NotEqual(lost.Value.RefreshToken, recovered.Value.RefreshToken);
        Assert.Equal(Now.AddHours(4).AddDays(14), recovered.Value.ExpiresAt);
        Assert.NotNull(repository.Rows.Single(x => x.TokenHash == Hash(lost.Value.RefreshToken)).RevokedAt);
        Assert.Null(repository.Rows.Single(x => x.TokenHash == Hash(original.RefreshToken)).RevokedAt);

        time.UtcNow = Now.AddHours(5);
        var next = await service.RefreshAsync(recovered.Value.RefreshToken, CancellationToken.None);
        Assert.Equal(ResultStatus.Ok, next.Status);
    }

    [Fact]
    public async Task RefreshAsync_WhenTheSupersededReplacementReturns_RevokesFamily()
    {
        var repository = new FakeRepository(User);
        var time = new FixedTimeProvider(Now);
        var service = CreateService(repository, time);
        var original = await service.StartAsync(User, CancellationToken.None);
        var superseded = await service.RefreshAsync(original.RefreshToken, CancellationToken.None);
        time.UtcNow = Now.AddHours(4);
        await service.RefreshAsync(original.RefreshToken, CancellationToken.None);

        time.UtcNow = Now.AddHours(5);
        var replay = await service.RefreshAsync(superseded.Value.RefreshToken, CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, replay.Status);
        Assert.All(repository.Rows, row => Assert.NotNull(row.RevokedAt));
    }

    [Fact]
    public async Task RefreshAsync_WithoutACookie_IsUnauthorizedWithoutTouchingSessions()
    {
        var repository = new FakeRepository(User);
        var service = CreateService(repository);

        var result = await service.RefreshAsync(null, CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        Assert.Empty(repository.Rows);
    }

    [Fact]
    public async Task RefreshAsync_WhenUserNoLongerExists_RevokesFamily()
    {
        var repository = new FakeRepository(User);
        var service = CreateService(repository);
        var original = await service.StartAsync(User, CancellationToken.None);
        repository.EligibleUser = null;

        var result = await service.RefreshAsync(original.RefreshToken, CancellationToken.None);

        Assert.Equal(ResultStatus.Unauthorized, result.Status);
        Assert.NotNull(Assert.Single(repository.Rows).RevokedAt);
    }

    [Fact]
    public async Task RevokeAsync_RevokesWholeFamily()
    {
        var repository = new FakeRepository(User);
        var service = CreateService(repository);
        var original = await service.StartAsync(User, CancellationToken.None);
        var rotated = await service.RefreshAsync(original.RefreshToken, CancellationToken.None);

        await service.RevokeAsync(rotated.Value.RefreshToken, CancellationToken.None);

        Assert.All(repository.Rows, row => Assert.NotNull(row.RevokedAt));
    }

    private static RefreshSessionService CreateService(FakeRepository repository, FixedTimeProvider? time = null) => new(
        repository,
        time ?? new FixedTimeProvider(Now),
        new RefreshSessionPolicy(
            AbsoluteLifetime: TimeSpan.FromDays(90),
            IdleLifetime: TimeSpan.FromDays(14),
            ConcurrentRefreshGrace: TimeSpan.FromSeconds(30),
            CleanupRetention: TimeSpan.FromDays(7)),
        NullLogger<RefreshSessionService>.Instance);

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class FakeRepository(AuthUserInfo user) : IRefreshSessionRepository
    {
        public List<RefreshSessionRow> Rows { get; } = [];
        public AuthUserInfo? EligibleUser { get; set; } = user;

        public Task CreateAsync(RefreshSessionRow session, CancellationToken cancellationToken)
        {
            Rows.Add(session);
            return Task.CompletedTask;
        }

        public Task<RefreshSessionRow?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.SingleOrDefault(x => x.TokenHash == tokenHash));

        public Task<AuthUserInfo?> GetEligibleUserAsync(Guid userId, Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(EligibleUser is not null
                && EligibleUser.UserId == userId
                && EligibleUser.OrganizationId == organizationId ? EligibleUser : null);

        public Task<bool> TryRotateAsync(RefreshSessionRow current, RefreshSessionRow replacement, DateTimeOffset usedAt, CancellationToken cancellationToken)
        {
            if (current.UsedAt is not null || current.RevokedAt is not null) return Task.FromResult(false);
            current.UsedAt = usedAt;
            current.ConcurrencyStamp = Guid.NewGuid();
            Rows.Add(replacement);
            return Task.FromResult(true);
        }

        public Task<RefreshSessionRow?> GetUnusedSuccessorAsync(RefreshSessionRow current, CancellationToken cancellationToken)
        {
            var candidates = Rows.Where(x => x.FamilyId == current.FamilyId
                && x.Id != current.Id
                && current.UsedAt is not null
                && x.CreatedAt == current.UsedAt
                && x.UsedAt is null
                && x.RevokedAt is null).Take(2).ToList();
            return Task.FromResult(candidates.Count == 1 ? candidates[0] : null);
        }

        public Task<bool> TryReplaceUnusedSuccessorAsync(RefreshSessionRow current, RefreshSessionRow successor, RefreshSessionRow replacement, DateTimeOffset usedAt, CancellationToken cancellationToken)
        {
            if (successor.UsedAt is not null || successor.RevokedAt is not null || current.RevokedAt is not null)
                return Task.FromResult(false);
            successor.RevokedAt = usedAt;
            successor.ConcurrencyStamp = Guid.NewGuid();
            current.UsedAt = usedAt;
            current.ConcurrencyStamp = Guid.NewGuid();
            Rows.Add(replacement);
            return Task.FromResult(true);
        }

        public Task<bool> TryClaimConcurrentRefreshGraceAsync(Guid sessionId, Guid expectedConcurrencyStamp, DateTimeOffset graceReuseAt, CancellationToken cancellationToken)
        {
            var row = Rows.Single(x => x.Id == sessionId);
            if (row.ConcurrencyStamp != expectedConcurrencyStamp || row.GraceReuseAt is not null || row.RevokedAt is not null)
                return Task.FromResult(false);
            row.GraceReuseAt = graceReuseAt;
            row.ConcurrencyStamp = Guid.NewGuid();
            return Task.FromResult(true);
        }

        public Task RevokeFamilyAsync(Guid familyId, DateTimeOffset revokedAt, CancellationToken cancellationToken)
        {
            foreach (var row in Rows.Where(x => x.FamilyId == familyId)) row.RevokedAt = revokedAt;
            return Task.CompletedTask;
        }

        public Task<int> DeleteExpiredFamiliesAsync(DateTimeOffset deleteBefore, CancellationToken cancellationToken)
        {
            var removed = Rows.RemoveAll(x => x.ExpiresAt <= deleteBefore || x.RevokedAt <= deleteBefore);
            return Task.FromResult(removed);
        }
    }
}
