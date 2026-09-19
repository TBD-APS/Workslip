using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Workslip.Api;
using Workslip.Api.Configuration;
using Workslip.Api.Helpers;
using Workslip.Application.Common;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Auth;

public sealed class UserClaimsTransformationRoleRefreshTests
{
    [Fact]
    public async Task TransformAsync_LocalSession_RefreshesRoleAndOrganizationFromWorkslipDatabase()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repository = new FakeUserRepository
        {
            ExternalUser = new UserDataRow
            {
                Id = userId,
                OrganizationId = organizationId,
                FilialId = Guid.NewGuid(),
                Email = "user@example.test",
                EntraEmail = "user@example.test",
                EntraId = "entra-user",
                DisplayName = "User",
                Phone = string.Empty,
                Role = Roles.Admin
            }
        };
        using var replica = new Replica(repository);
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Email, "user@example.test"),
                new Claim("organizationId", Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, Roles.User)
            ],
            authenticationType: "LocalJwt");
        var principal = new ClaimsPrincipal(identity);

        var transformed = await replica.Transformation.TransformAsync(principal);

        Assert.Equal(1, repository.ExternalIdentityCalls);
        Assert.Equal(organizationId.ToString(), transformed.FindFirst("organizationId")?.Value);
        Assert.Equal(userId.ToString(), transformed.FindFirst("workslipUserId")?.Value);
        Assert.Equal(Roles.Admin, transformed.FindFirst(ClaimTypes.Role)?.Value);
        Assert.DoesNotContain(transformed.Claims, claim => claim.Type == ClaimTypes.Role && claim.Value == Roles.User);
    }

    [Fact]
    public async Task TransformAsync_DelegatedSuperadminSession_PreservesEffectiveTenantClaimsWithoutDatabaseLookup()
    {
        var effectiveOrganizationId = Guid.NewGuid();
        var repository = new FakeUserRepository();
        using var replica = new Replica(repository);
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Email, "superadmin@example.test"),
                new Claim("organizationId", effectiveOrganizationId.ToString()),
                new Claim(ClaimTypes.Role, Roles.Superadmin),
                new Claim(JwtHelper.DelegatedOrganizationSessionClaim, bool.TrueString.ToLowerInvariant())
            ],
            authenticationType: "LocalJwt");
        var principal = new ClaimsPrincipal(identity);

        var transformed = await replica.Transformation.TransformAsync(principal);

        Assert.Equal(0, repository.ExternalIdentityCalls);
        Assert.Equal(effectiveOrganizationId.ToString(), transformed.FindFirst("organizationId")?.Value);
        Assert.Equal(Roles.Superadmin, transformed.FindFirst(ClaimTypes.Role)?.Value);
    }

    [Fact]
    public async Task TransformAsync_LocalSessionWithoutDatabaseUser_RemovesWorkslipAuthorizationClaims()
    {
        var repository = new FakeUserRepository();
        using var replica = new Replica(repository);
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Email, "deleted@example.test"),
                new Claim("organizationId", Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Role, Roles.Admin),
                new Claim("roles", Roles.Admin)
            ],
            authenticationType: "LocalJwt");
        var principal = new ClaimsPrincipal(identity);

        var transformed = await replica.Transformation.TransformAsync(principal);

        Assert.Equal(1, repository.ExternalIdentityCalls);
        Assert.True(transformed.Identity?.IsAuthenticated);
        Assert.NotNull(transformed.FindFirst(ClaimTypes.Email));
        Assert.DoesNotContain(transformed.Claims, claim => claim.Type == "organizationId");
        Assert.DoesNotContain(transformed.Claims, claim => claim.Type == "workslipUserId");
        Assert.DoesNotContain(transformed.Claims, claim => claim.Type == ClaimTypes.Role);
        Assert.DoesNotContain(transformed.Claims, claim => claim.Type == "roles");
    }

    /// <summary>
    /// The property the whole design now rests on: with a distributed cache registered
    /// as HybridCache's L2, resolving and invalidating a user's claims never names a
    /// claims key to that shared tier at all - not as a write, not as a read, not as a
    /// delete. Asserted over every key the shared store is asked about rather than only
    /// over what it ends up holding, because a delete of a key that is never written
    /// still puts an e-mail address on the wire, and that is the thing being ruled out.
    /// </summary>
    [Fact]
    public async Task TransformAsync_AndInvalidate_NeverNameAClaimsKeyToTheSharedTier()
    {
        var row = CreateUserRow(Roles.Admin);
        var repository = new FakeUserRepository { ExternalUser = row };
        var sharedL2 = new RecordingDistributedCache();
        using var replica = new Replica(repository, sharedL2);

        Assert.Equal(Roles.Admin, await ResolveRoleAsync(replica, row));
        replica.Invalidator.Invalidate(row.EntraId, row.Email, row.EntraEmail);
        Assert.Equal(Roles.Admin, await ResolveRoleAsync(replica, row));

        // Guards against a vacuous assertion: HybridCache must really be wired to this
        // store, which it proves by reading its tag-invalidation markers. If this ever
        // fails, the fixture stopped registering an L2 and the rest proves nothing.
        Assert.NotEmpty(sharedL2.KeysTouched);

        Assert.DoesNotContain(sharedL2.KeysTouched, key => key.StartsWith("auth:user:", StringComparison.Ordinal));
        Assert.DoesNotContain(sharedL2.StoredKeys, key => key.StartsWith("auth:user:", StringComparison.Ordinal));

        // The hard rule, asserted on its own terms rather than through the key prefix:
        // no personal data reaches a Redis key. The claims keys are the only keys in
        // this cache that embed an e-mail address.
        Assert.DoesNotContain(sharedL2.KeysTouched, key => key.Contains('@', StringComparison.Ordinal));
    }

    /// <summary>
    /// The cost this design accepts in exchange, stated as a test so it is a decision
    /// and not a surprise: a second replica does not get the first one's entry, so it
    /// repeats the database read. That saved read is the entire value the withdrawn
    /// shared-claims design bought.
    /// </summary>
    [Fact]
    public async Task TransformAsync_OnASecondReplicaSharingTheDistributedCache_ResolvesFromTheDatabaseItself()
    {
        var row = CreateUserRow(Roles.Admin);
        var repository = new FakeUserRepository { ExternalUser = row };
        var sharedL2 = new RecordingDistributedCache();
        using var replicaA = new Replica(repository, sharedL2);
        using var replicaB = new Replica(repository, sharedL2);

        Assert.Equal(Roles.Admin, await ResolveRoleAsync(replicaA, row));
        Assert.Equal(Roles.Admin, await ResolveRoleAsync(replicaB, row));

        Assert.Equal(2, repository.ExternalIdentityCalls);
    }

    /// <summary>
    /// What the invalidator is still for: the operator who changed the role is talking
    /// to one replica, and that replica must stop serving the old claims on the very
    /// next request.
    ///
    /// This is also the test that pins the invalidator's one implementation coupling.
    /// It writes through the real <see cref="HybridCache"/> with the production entry
    /// options and reads back through it, while the invalidator removes the entry from
    /// the container's <see cref="IMemoryCache"/>; that only works because HybridCache's
    /// L1 is that same cache, keyed by the unmodified cache key. A package upgrade that
    /// changed either would make the invalidation a silent no-op, and would fail here.
    /// </summary>
    [Fact]
    public async Task Invalidate_OnTheReplicaThatServedTheChange_TakesEffectOnTheNextRequest()
    {
        var row = CreateUserRow(Roles.Admin);
        var repository = new FakeUserRepository { ExternalUser = row };
        using var replica = new Replica(repository);

        Assert.Equal(Roles.Admin, await ResolveRoleAsync(replica, row));
        Assert.Equal(Roles.Admin, await ResolveRoleAsync(replica, row));
        Assert.Equal(1, repository.ExternalIdentityCalls);

        row.Role = Roles.User;
        replica.Invalidator.Invalidate(row.EntraId, row.Email, row.EntraEmail);

        Assert.Equal(Roles.User, await ResolveRoleAsync(replica, row));
        Assert.Equal(2, repository.ExternalIdentityCalls);
    }

    /// <summary>
    /// The limitation stated in the open, because it is the one an operator has to know:
    /// there is no immediate cross-replica revocation. A replica that did not serve the
    /// change keeps honouring the old role until its own copy lapses, and then converges
    /// by re-reading the database - there is no shared row to converge through, which is
    /// exactly the trade this design makes.
    /// </summary>
    [Fact]
    public async Task Invalidate_DoesNotReachAnotherReplica_WhichConvergesWhenItsLocalCopyLapses()
    {
        var row = CreateUserRow(Roles.Admin);
        var repository = new FakeUserRepository { ExternalUser = row };
        using var replicaA = new Replica(repository);
        using var replicaB = new Replica(repository);

        Assert.Equal(Roles.Admin, await ResolveRoleAsync(replicaA, row));
        Assert.Equal(Roles.Admin, await ResolveRoleAsync(replicaB, row));

        row.Role = Roles.User;
        replicaA.Invalidator.Invalidate(row.EntraId, row.Email, row.EntraEmail);

        // Immediate where the change was made.
        Assert.Equal(Roles.User, await ResolveRoleAsync(replicaA, row));

        // Not on the other replica. Pinned on purpose: if this ever starts failing,
        // something gained a backplane and the documentation is out of date.
        Assert.Equal(Roles.Admin, await ResolveRoleAsync(replicaB, row));

        // What bounds it is one lifetime, the same value everywhere. Dropping replica
        // B's local entries is what MemoryCache does when their absolute expiration
        // passes.
        Assert.Equal(TimeSpan.FromMinutes(1), UserClaimsCache.Lifetime);
        replicaB.LocalCache.Remove(EntraKey(row));
        replicaB.LocalCache.Remove(EmailKey(row));

        Assert.Equal(Roles.User, await ResolveRoleAsync(replicaB, row));
    }

    /// <summary>
    /// The interleaving that three verification rounds kept attacking, now stated for
    /// what it actually is once claims are process-local. A resolution that read the
    /// pre-change role and writes it after the invalidation does keep the old role in
    /// this process - that is real, and it is why the invalidator cannot promise
    /// immediacy in the presence of a concurrent request for the same user.
    ///
    /// It needs no generation token, no capture-before-read and no write-then-verify,
    /// because it cannot be wider or longer than the bound that already applies
    /// everywhere: one process, one lifetime. This test pins both halves - the honest
    /// consequence, and that it reaches neither the shared tier nor a replica that
    /// starts afterwards.
    /// </summary>
    [Fact]
    public async Task TransformAsync_WhenAnInvalidationOvertakesAnInFlightResolution_KeepsThePreChangeRoleNoWiderThanThisProcess()
    {
        var row = CreateUserRow(Roles.Admin);
        var repository = new FakeUserRepository { ExternalUser = row };
        var sharedL2 = new RecordingDistributedCache();
        using var resolving = new Replica(repository, sharedL2);

        // The demotion commits and invalidates while this resolution is holding the row
        // it read a moment earlier.
        repository.WhileResolving = () =>
        {
            row.Role = Roles.User;
            resolving.Invalidator.Invalidate(row.EntraId, row.Email, row.EntraEmail);
            return Task.CompletedTask;
        };

        // The in-flight request is answered from what it read - it was authorized
        // against that row.
        Assert.Equal(Roles.Admin, await ResolveRoleAsync(resolving, row));

        // And that value is now cached here, so this replica keeps answering it. The
        // consequence of dropping the withdrawn publish-safety check, pinned rather
        // than hidden.
        Assert.Equal(Roles.Admin, await ResolveRoleAsync(resolving, row));
        Assert.Equal(1, repository.ExternalIdentityCalls);

        // It is bounded by one lifetime, like every other replica's copy.
        resolving.LocalCache.Remove(EntraKey(row));
        resolving.LocalCache.Remove(EmailKey(row));
        Assert.Equal(Roles.User, await ResolveRoleAsync(resolving, row));

        // And it never left this process: nothing was published to the shared tier, so
        // a replica that starts afterwards - a scale-out, a restart, a rolling revision
        // - reads the database and sees the change.
        Assert.DoesNotContain(sharedL2.KeysTouched, key => key.StartsWith("auth:user:", StringComparison.Ordinal));
        using var startedAfterTheChange = new Replica(repository, sharedL2);
        Assert.Equal(Roles.User, await ResolveRoleAsync(startedAfterTheChange, row));
    }

    /// <summary>
    /// The entry options are the whole enforcement mechanism, so they are asserted bit
    /// by bit rather than trusted to a flag name. <c>DisableDistributedCache</c> is read
    /// and write together; no <c>DisableLocalCache*</c> bit may be set, because that
    /// would disable the only tier left; and both expirations carry the one lifetime
    /// because HybridCache uses the smaller of the two as the local one.
    /// </summary>
    [Fact]
    public void ClaimsCacheEntryOptions_AreProcessLocalWithASingleLifetime()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), UserClaimsCache.Lifetime);

        foreach (var options in new[] { UserClaimsCache.EntryOptions, UserClaimsCache.ProbeOptions })
        {
            var flags = options.Flags ?? HybridCacheEntryFlags.None;

            Assert.True(flags.HasFlag(HybridCacheEntryFlags.DisableDistributedCacheWrite), "the payload must never be written to the shared tier");
            Assert.True(flags.HasFlag(HybridCacheEntryFlags.DisableDistributedCacheRead), "the key must never be read from the shared tier");
            Assert.False(flags.HasFlag(HybridCacheEntryFlags.DisableLocalCacheRead), "L1 is the only tier these entries have");
            Assert.False(flags.HasFlag(HybridCacheEntryFlags.DisableLocalCacheWrite), "L1 is the only tier these entries have");

            Assert.Equal(UserClaimsCache.Lifetime, options.LocalCacheExpiration);
            Assert.Equal(UserClaimsCache.Lifetime, options.Expiration);
        }

        // The probe differs from the write in exactly one bit: it must not invoke the
        // factory, so a miss returns default(T) instead of loading anything.
        Assert.False(
            (UserClaimsCache.EntryOptions.Flags ?? HybridCacheEntryFlags.None).HasFlag(HybridCacheEntryFlags.DisableUnderlyingData));
        Assert.True(
            (UserClaimsCache.ProbeOptions.Flags ?? HybridCacheEntryFlags.None).HasFlag(HybridCacheEntryFlags.DisableUnderlyingData));
    }

    /// <summary>
    /// What the superadmin cache screen is told about the claims region has to be true.
    /// The TTL used to differ between the two configurations because the local lifetime
    /// did; it no longer can, because these entries are process-local whether or not a
    /// distributed cache is registered. Asserted against the definitions
    /// <see cref="ServiceConfiguration"/> builds, because a parametrised copy of them is
    /// what let the region ship labelled as a process-local IMemoryCache after it had
    /// moved onto HybridCache.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildCacheRegions_ReportsTheSameClaimsTtlInBothConfigurations(bool distributedCacheConfigured)
    {
        var claims = ServiceConfiguration.BuildCacheRegions(distributedCacheConfigured)
            .Single(region => region.Name == CacheRegionNames.AuthenticatedUsers);

        // CacheStoreTypes.Hybrid is the literal CacheReach.SupportsDistributedTier
        // compares against. It stays Hybrid because that is the store the region is
        // registered against - the entries opt out of the shared tier per call, which
        // the region definition has no way to express today.
        Assert.Equal(CacheStoreTypes.Hybrid, claims.Type);
        Assert.Equal((int)UserClaimsCache.Lifetime.TotalSeconds, claims.TtlSeconds);
    }

    /// <summary>
    /// Authentication must not depend on the cache being usable.
    /// </summary>
    [Fact]
    public async Task TransformAsync_WhenCacheThrows_ResolvesClaimsFromTheDatabase()
    {
        var row = CreateUserRow(Roles.Admin);
        var repository = new FakeUserRepository { ExternalUser = row };
        var diagnostics = CreateDiagnostics();
        var transformation = new UserClaimsTransformation(
            repository,
            new FaultyHybridCache(),
            diagnostics,
            NullLogger<UserClaimsTransformation>.Instance);

        var transformed = await transformation.TransformAsync(CreatePrincipal(row));

        Assert.Equal(1, repository.ExternalIdentityCalls);
        Assert.Equal(Roles.Admin, transformed.FindFirst(ClaimTypes.Role)?.Value);
        Assert.Equal(row.Id.ToString(), transformed.FindFirst("workslipUserId")?.Value);
        Assert.True(AuthenticatedUserRegion(diagnostics).Failures > 0, "a cache fault must be observable");
    }

    /// <summary>
    /// The database write has already committed when Invalidate runs, so a cache that
    /// refuses the removal must not turn a successful role change into a failed request.
    /// Unreachable in practice now that the removal is an in-process dictionary
    /// operation - a disposed cache during host shutdown is about the only way in - but
    /// the guarantee is about the committed write, not about the likelihood.
    /// </summary>
    [Fact]
    public void Invalidate_WhenTheLocalCacheThrows_DoesNotFailTheRoleChange()
    {
        var diagnostics = CreateDiagnostics();
        var invalidator = new UserClaimsCacheInvalidator(
            new FaultyMemoryCache(),
            diagnostics,
            NullLogger<UserClaimsCacheInvalidator>.Instance);

        invalidator.Invalidate("entra-faulty", "faulty@example.test", "faulty@example.test");

        Assert.Equal(1, AuthenticatedUserRegion(diagnostics).Failures);
        Assert.Equal(0, AuthenticatedUserRegion(diagnostics).Invalidations);
    }

    private static async Task<string?> ResolveRoleAsync(Replica replica, UserDataRow row)
    {
        var transformed = await replica.Transformation.TransformAsync(CreatePrincipal(row));

        // Guards against a vacuous assertion: a transformation that bailed out would
        // leave the incoming claims (Roles.Auditor, no workslipUserId) in place.
        Assert.Equal(row.Id.ToString(), transformed.FindFirst("workslipUserId")?.Value);
        return transformed.FindFirst(ClaimTypes.Role)?.Value;
    }

    private static ClaimsPrincipal CreatePrincipal(UserDataRow row) =>
        new(new ClaimsIdentity(
            [
                new Claim("oid", row.EntraId),
                new Claim(ClaimTypes.Email, row.Email),
                new Claim("organizationId", row.OrganizationId.ToString()),

                // Never an expected outcome, so "the transformation did nothing" can
                // never be mistaken for "the role resolved to this".
                new Claim(ClaimTypes.Role, Roles.Auditor)
            ],
            authenticationType: "Entra"));

    private static UserDataRow CreateUserRow(string role) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = Guid.NewGuid(),
        FilialId = Guid.NewGuid(),
        Email = "role.change@example.test",
        EntraEmail = "role.change@example.test",
        EntraId = "entra-role-change",
        DisplayName = "Role Change",
        Phone = string.Empty,
        Role = role
    };

    // Key shape is part of the contract: UserClaimsCacheInvalidator removes exactly
    // these keys, and nothing else in the backend reads them.
    private static string EntraKey(UserDataRow row) => $"auth:user:entra:{row.EntraId}";

    private static string EmailKey(UserDataRow row) => $"auth:user:email:{row.Email}";

    // The definitions the API actually registers, not a copy of them: a hand-copied
    // region definition is what let the claims region ship mislabelled.
    private static CacheDiagnostics CreateDiagnostics() =>
        new(ServiceConfiguration.BuildCacheRegions(distributedCacheConfigured: true));

    private static CacheRegionSnapshot AuthenticatedUserRegion(CacheDiagnostics diagnostics) =>
        diagnostics.GetSnapshot().Regions.Single(region => region.Name == CacheRegionNames.AuthenticatedUsers);

    /// <summary>
    /// One API process: its own HybridCache and its own L1, optionally sharing a
    /// distributed cache with the other replicas in the test - the same topology as
    /// Container Apps running several replicas against one Redis.
    /// </summary>
    private sealed class Replica : IDisposable
    {
        private readonly ServiceProvider _services;

        internal Replica(IUserRepository repository, IDistributedCache? sharedDistributedCache = null)
        {
            var services = new ServiceCollection();
            if (sharedDistributedCache is not null)
            {
                services.AddSingleton(sharedDistributedCache);
            }

            services.AddHybridCache();
            _services = services.BuildServiceProvider();

            var cache = _services.GetRequiredService<HybridCache>();
            var diagnostics = CreateDiagnostics();

            // The same instance HybridCache uses as its L1, which is what makes the
            // invalidator's removal reach the entry the transformation wrote.
            LocalCache = _services.GetRequiredService<IMemoryCache>();

            Transformation = new UserClaimsTransformation(
                repository,
                cache,
                diagnostics,
                NullLogger<UserClaimsTransformation>.Instance);
            Invalidator = new UserClaimsCacheInvalidator(
                LocalCache,
                diagnostics,
                NullLogger<UserClaimsCacheInvalidator>.Instance);
        }

        internal IMemoryCache LocalCache { get; }

        internal UserClaimsTransformation Transformation { get; }

        internal UserClaimsCacheInvalidator Invalidator { get; }

        public void Dispose() => _services.Dispose();
    }

    /// <summary>
    /// Stand-in for the shared Redis L2, recording every key it is asked about so a
    /// test can assert what did and did not reach it - including operations that leave
    /// nothing behind, such as a delete.
    /// </summary>
    private sealed class RecordingDistributedCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _entries = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> _keysTouched = new(StringComparer.Ordinal);

        internal IReadOnlyCollection<string> KeysTouched => _keysTouched.Keys.ToArray();

        internal IReadOnlyCollection<string> StoredKeys => _entries.Keys.ToArray();

        public byte[]? Get(string key)
        {
            Record(key);
            return _entries.TryGetValue(key, out var value) ? value : null;
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

        public void Refresh(string key) => Record(key);

        public Task RefreshAsync(string key, CancellationToken token = default)
        {
            Refresh(key);
            return Task.CompletedTask;
        }

        public void Remove(string key)
        {
            Record(key);
            _entries.TryRemove(key, out _);
        }

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            Record(key);
            _entries[key] = value;
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        private void Record(string key) => _keysTouched.TryAdd(key, 0);
    }

    /// <summary>A cache that is completely unusable.</summary>
    private sealed class FaultyHybridCache : HybridCache
    {
        public override ValueTask<T> GetOrCreateAsync<TState, T>(
            string key,
            TState state,
            Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("cache unavailable");

        public override ValueTask SetAsync<T>(
            string key,
            T value,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("cache unavailable");

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("cache unavailable");

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("cache unavailable");
    }

    /// <summary>An L1 that refuses every operation, the way a disposed one would.</summary>
    private sealed class FaultyMemoryCache : IMemoryCache
    {
        public ICacheEntry CreateEntry(object key) => throw new ObjectDisposedException(nameof(MemoryCache));

        public void Remove(object key) => throw new ObjectDisposedException(nameof(MemoryCache));

        public bool TryGetValue(object key, out object? value) => throw new ObjectDisposedException(nameof(MemoryCache));

        public void Dispose()
        {
        }
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public UserDataRow? ExternalUser { get; init; }
        public int ExternalIdentityCalls { get; private set; }

        /// <summary>
        /// Runs once, after a claims resolution has read the user row and before it can
        /// cache what it read. That gap is where a role change commits and invalidates
        /// underneath a request that is already holding the pre-change row.
        /// </summary>
        public Func<Task>? WhileResolving { get; set; }

        public async Task<UserDataRow?> GetByExternalIdentityAsync(
            string? entraId,
            IReadOnlyCollection<string> emailCandidates,
            CancellationToken cancellationToken)
        {
            ExternalIdentityCalls++;

            var hook = WhileResolving;
            if (hook is null)
            {
                return ExternalUser;
            }

            // The caller must go on to see the row as of this read, not whatever the
            // hook changes it into - otherwise the resolution would pick up the new role
            // for free and there would be no lost update to test.
            var readAsOf = ExternalUser is null ? null : CopyOf(ExternalUser);
            WhileResolving = null;
            await hook();
            return readAsOf;
        }

        private static UserDataRow CopyOf(UserDataRow row) => new()
        {
            Id = row.Id,
            OrganizationId = row.OrganizationId,
            FilialId = row.FilialId,
            Email = row.Email,
            DisplayName = row.DisplayName,
            EntraId = row.EntraId,
            EntraEmail = row.EntraEmail,
            Phone = row.Phone,
            Role = row.Role,
            UserKind = row.UserKind,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };

        public Task<UserDataRow?> GetAuthenticatedActorAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UserDataRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UserDataRow?> GetByEmailAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<UserDataRow>> GetByOrganizationIdAsync(Guid organizationId, int limit, int offset, string? search, string? sortBy, string? sortDirection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> GetCountByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Guid> CreateAsync(UserDataRow user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UpdateAsync(UserDataRow user, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<AssignedJobResponse>> GetAssignedJobsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<decimal?> GetTotalHoursAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, UserPeriodHours>> GetPeriodHoursAsync(Guid organizationId, DateOnly biweeklyStart, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
