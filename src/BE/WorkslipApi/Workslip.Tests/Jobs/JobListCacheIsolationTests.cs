using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Domain;
using Xunit;

namespace Workslip.Tests.Jobs;

/// <summary>
/// Job list results are cached through HybridCache, so the strings these tests inspect are
/// the Redis key <em>names</em> once a distributed second level is registered. Two
/// properties have to hold together: a key may not carry customer data in plaintext, and
/// two different queries may never map to one key - a merge would serve one result set for
/// two searches, and if the differing component were the organization or the viewer that
/// is a cross-tenant leak.
/// </summary>
public sealed class JobListCacheIsolationTests
{
    // The plaintext the tests search with. No key may contain any of it.
    private const string ReportNumberFilter = "2024-0042";
    private const string CustomerNameFilter = "Ingrid Sørensen";
    private const string CustomerEmailFilter = "ingrid@example.dk";
    private const string CustomerAddressFilter = "Nørrebrogade 12, 2200 København N";
    private const string SearchFilter = "tag:vvs";

    [Fact]
    public async Task ListAsync_DoesNotReuseSeenStateAcrossUsers()
    {
        var organizationId = Guid.NewGuid();
        var seenUserId = Guid.NewGuid();
        var unseenUserId = Guid.NewGuid();
        var repository = new UserAwareJobRepository(seenUserId);

        using var services = CreateCacheServices();
        var cache = services.GetRequiredService<HybridCache>();
        var seenUserService = CreateService(repository, cache, organizationId, seenUserId);
        var unseenUserService = CreateService(repository, cache, organizationId, unseenUserId);

        var seenResult = await ListDraftsAsync(seenUserService);
        var unseenResult = await ListDraftsAsync(unseenUserService);

        Assert.True(seenResult.IsSuccess);
        Assert.True(unseenResult.IsSuccess);
        Assert.True(seenResult.Value.Items.Single().IsSeenByCurrentUser);
        Assert.False(unseenResult.Value.Items.Single().IsSeenByCurrentUser);
        Assert.Equal(2, repository.ListCallCount);
        Assert.Contains(repository.Queries, query => query.AssignedToUserId == seenUserId);
        Assert.Contains(repository.Queries, query => query.AssignedToUserId == unseenUserId);
    }

    [Fact]
    public async Task ListAsync_ReusesCacheForTheSameUser()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repository = new UserAwareJobRepository(userId);

        using var services = CreateCacheServices();
        var service = CreateService(
            repository,
            services.GetRequiredService<HybridCache>(),
            organizationId,
            userId);

        await ListDraftsAsync(service);
        await ListDraftsAsync(service);

        Assert.Equal(1, repository.ListCallCount);
    }

    /// <summary>
    /// The same viewer id in two organizations, so the organization is the only component
    /// that differs. A key that merged them would hand one tenant the other tenant's rows.
    /// </summary>
    [Fact]
    public async Task ListAsync_DoesNotServeOneOrganizationsRowsToAnother()
    {
        var firstOrganizationId = Guid.NewGuid();
        var secondOrganizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repository = new UserAwareJobRepository(userId);

        using var services = CreateCacheServices();
        var cache = services.GetRequiredService<HybridCache>();

        var first = await ListDraftsAsync(CreateService(repository, cache, firstOrganizationId, userId));
        var second = await ListDraftsAsync(CreateService(repository, cache, secondOrganizationId, userId));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(firstOrganizationId, first.Value.Items.Single().OrganizationId);
        Assert.Equal(secondOrganizationId, second.Value.Items.Single().OrganizationId);
        Assert.Equal(2, repository.ListCallCount);
    }

    /// <summary>
    /// The pair that made this change necessary. The previous key pasted each filter into
    /// "...:customerName={value}:customerEmail={value}:..." and wrote the literal "none"
    /// for an unset filter, so a value carrying the next field's own name and that literal
    /// produced a byte-identical key for two different searches. Run through the real
    /// cache, so a scheme that merged them again would show up as a single repository call.
    /// </summary>
    [Fact]
    public async Task ListAsync_DoesNotServeOneCachedResultSetForTwoDifferentSearches()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repository = new UserAwareJobRepository(userId);

        using var services = CreateCacheServices();
        var cache = services.GetRequiredService<HybridCache>();
        var service = CreateService(repository, cache, organizationId, userId);

        var first = await ListJobsAsync(service, customerName: "aa:customerEmail=bb");
        var second = await ListJobsAsync(service, customerName: "aa", customerEmail: "bb:customerEmail=none");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);

        Assert.Equal(2, repository.ListCallCount);
        Assert.Contains(repository.Queries, query => query.CustomerName == "aa:customerEmail=bb");
        Assert.Contains(repository.Queries, query => query.CustomerEmail == "bb:customerEmail=none");
    }

    [Fact]
    public async Task CacheKey_CarriesNoCustomerPersonalDataInPlaintext()
    {
        var key = await CacheKeyAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Roles.User,
            statuses: [JobStatus.Draft],
            reportNumber: ReportNumberFilter,
            customerName: CustomerNameFilter,
            customerEmail: CustomerEmailFilter,
            customerAddress: CustomerAddressFilter,
            search: SearchFilter);

        // An e-mail address cannot survive in any shape without this character.
        Assert.DoesNotContain("@", key);

        string[] plaintext =
        [
            ReportNumberFilter,
            CustomerNameFilter,
            CustomerEmailFilter,
            CustomerAddressFilter,
            SearchFilter,
            // and no fragment of them either, so a partial leak cannot pass
            "Ingrid",
            "Sørensen",
            "example.dk",
            "Nørrebrogade",
            "København",
            "vvs"
        ];

        foreach (var value in plaintext)
        {
            Assert.DoesNotContain(value, key, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The whole key for a fixed query, so the readable half stays legible to an operator
    /// reading the keyspace and the fingerprint stays what it is. The expected digest was
    /// derived outside the implementation from the framing rule in
    /// <c>JobService.JobListQueryFingerprint</c>: SHA-256 over each component written at a
    /// fixed width or as a 4-byte little-endian UTF-8 byte count (-1 for null) followed by
    /// its bytes. Pinning it also pins that the digest is stable across processes, which a
    /// randomised hash such as <c>string.GetHashCode</c> would not be - and an L2 shared
    /// by several replicas needs that.
    /// </summary>
    [Fact]
    public async Task CacheKey_KeepsOrganizationAndQueryShapeReadable()
    {
        var organizationId = Guid.Parse("3f1a7c92-5b4d-4e8a-9f0b-6c7d8e9f0a1b");
        var userId = Guid.Parse("7e6d5c4b-3a29-4180-b7c6-d5e4f3a2b1c0");

        var key = await CacheKeyAsync(
            organizationId,
            userId,
            Roles.User,
            statuses: [JobStatus.Draft],
            reportNumber: ReportNumberFilter,
            customerName: CustomerNameFilter,
            customerEmail: CustomerEmailFilter,
            customerAddress: CustomerAddressFilter,
            search: SearchFilter,
            sortBy: "updatedAt",
            sortDirection: "DESC");

        Assert.Equal(
            "jobs:list:organization=3f1a7c925b4d4e8a9f0b6c7d8e9f0a1b" +
            ":currentUser=7e6d5c4b3a294180b7c6d5e4f3a2b1c0" +
            ":assignedTo=7e6d5c4b3a294180b7c6d5e4f3a2b1c0" +
            ":status=Draft:sort=updatedAt.desc:limit=20:offset=0" +
            ":filters=report+name+email+address+search" +
            ":query=4c562acb075f060748fee0d85a6f8320",
            key);
    }

    /// <summary>
    /// Two viewers who share every other component, with a role that takes the assignment
    /// scope out of the key, so only the viewer id separates them. The list carries
    /// per-viewer state (seen, new rejection), so a merge here shows one user another
    /// user's state.
    /// </summary>
    [Fact]
    public async Task CacheKey_SeparatesViewersWhoShareEveryOtherComponent()
    {
        var organizationId = Guid.NewGuid();

        var first = await CacheKeyAsync(organizationId, Guid.NewGuid(), Roles.Admin);
        var second = await CacheKeyAsync(organizationId, Guid.NewGuid(), Roles.Admin);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task CacheKey_SeparatesOrganizationsForTheSameViewer()
    {
        var userId = Guid.NewGuid();

        var first = await CacheKeyAsync(Guid.NewGuid(), userId, Roles.Admin);
        var second = await CacheKeyAsync(Guid.NewGuid(), userId, Roles.Admin);

        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// One key per distinct query, including values chosen to forge a component boundary:
    /// the key delimiters, the "none" and "all" sentinels the readable half uses, a value
    /// shaped like an organization id, and a value carrying another component's name.
    /// Two of the entries are the pair that collided under the previous delimited key.
    /// </summary>
    [Fact]
    public async Task CacheKey_CannotBeMadeAmbiguousByAdversarialInput()
    {
        var organizationId = Guid.NewGuid();
        var otherOrganizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();

        List<(string Label, string Key)> keys =
        [
            ("baseline", await CacheKeyAsync(organizationId, userId, Roles.Admin)),
            ("other organization", await CacheKeyAsync(otherOrganizationId, userId, Roles.Admin)),
            ("other viewer", await CacheKeyAsync(organizationId, otherUserId, Roles.Admin)),
            ("assigned-only scope", await CacheKeyAsync(organizationId, userId, Roles.User)),
            ("one status", await CacheKeyAsync(organizationId, userId, Roles.Admin, statuses: [JobStatus.Draft])),
            ("another status", await CacheKeyAsync(organizationId, userId, Roles.Admin, statuses: [JobStatus.Approved])),
            ("two statuses", await CacheKeyAsync(organizationId, userId, Roles.Admin, statuses: [JobStatus.Draft, JobStatus.Approved])),
            ("next page", await CacheKeyAsync(organizationId, userId, Roles.Admin, offset: 20)),
            ("larger page", await CacheKeyAsync(organizationId, userId, Roles.Admin, limit: 21)),
            ("named sort column", await CacheKeyAsync(organizationId, userId, Roles.Admin, sortBy: "name")),
            ("named sort column ascending", await CacheKeyAsync(organizationId, userId, Roles.Admin, sortBy: "name", sortDirection: "asc")),
            ("unnamed sort column", await CacheKeyAsync(organizationId, userId, Roles.Admin, sortBy: "customerEmail")),
            ("another unnamed sort column", await CacheKeyAsync(organizationId, userId, Roles.Admin, sortBy: "customerName")),
            ("report filter", await CacheKeyAsync(organizationId, userId, Roles.Admin, reportNumber: "42")),
            ("name filter", await CacheKeyAsync(organizationId, userId, Roles.Admin, customerName: "42")),
            ("e-mail filter", await CacheKeyAsync(organizationId, userId, Roles.Admin, customerEmail: "42")),
            ("address filter", await CacheKeyAsync(organizationId, userId, Roles.Admin, customerAddress: "42")),
            ("search filter", await CacheKeyAsync(organizationId, userId, Roles.Admin, search: "42")),
            ("name carrying the next component's name", await CacheKeyAsync(organizationId, userId, Roles.Admin, customerName: "aa:customerEmail=bb")),
            ("name and e-mail split at that boundary", await CacheKeyAsync(organizationId, userId, Roles.Admin, customerName: "aa", customerEmail: "bb:customerEmail=none")),
            ("name spelling the unset sentinel", await CacheKeyAsync(organizationId, userId, Roles.Admin, customerName: "none")),
            ("name spelling the whole-organization sentinel", await CacheKeyAsync(organizationId, userId, Roles.Admin, customerName: "all")),
            ("name shaped like an organization id", await CacheKeyAsync(organizationId, userId, Roles.Admin, customerName: otherOrganizationId.ToString("N"))),
            ("name forging a viewer boundary", await CacheKeyAsync(organizationId, userId, Roles.Admin, customerName: $"x:currentUser={otherUserId:N}:assignedTo=all")),
            ("search forging the whole tail", await CacheKeyAsync(organizationId, userId, Roles.Admin, search: "x:sort=default:limit=20:offset=0:filters=none")),
            ("empty-string filter is the same as unset", await CacheKeyAsync(organizationId, userId, Roles.Admin, customerName: "  "))
        ];

        var collisions = keys
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(" == ", group.Select(entry => entry.Label)))
            .ToArray();

        // Whitespace normalises to unset upstream, in the query as well as in the key, so
        // that one entry is expected to share the baseline key and nothing else may.
        Assert.Equal("baseline == empty-string filter is the same as unset", Assert.Single(collisions));
    }

    /// <summary>
    /// The key no longer grows with the text a caller sends, which keeps it inside
    /// HybridCache's <c>MaximumKeyLength</c> (1024 characters by default) however long a
    /// search term is, and keeps a Redis keyspace readable.
    /// </summary>
    [Fact]
    public async Task CacheKey_StaysBoundedForOversizedInput()
    {
        var key = await CacheKeyAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Roles.User,
            statuses: [.. Enumerable.Repeat(JobStatus.Draft, 500)],
            reportNumber: new string('r', 2000),
            customerName: new string('n', 2000),
            customerEmail: new string('e', 2000),
            customerAddress: new string('a', 2000),
            search: new string('s', 2000),
            sortBy: new string('x', 2000),
            sortDirection: new string('y', 2000));

        Assert.True(key.Length < 400, $"Cache key grew to {key.Length} characters: {key}");
    }

    private static ServiceProvider CreateCacheServices()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Runs one list query and returns the key the service asked the cache for - the string
    /// that becomes a Redis key name once a distributed second level is registered.
    /// </summary>
    private static async Task<string> CacheKeyAsync(
        Guid organizationId,
        Guid userId,
        string role,
        List<JobStatus>? statuses = null,
        string? reportNumber = null,
        string? customerName = null,
        string? customerEmail = null,
        string? customerAddress = null,
        string? search = null,
        string? sortBy = null,
        string? sortDirection = null,
        int? limit = 20,
        int? offset = 0)
    {
        using var services = CreateCacheServices();
        var recording = new RecordingHybridCache(services.GetRequiredService<HybridCache>());
        var service = CreateService(
            new UserAwareJobRepository(userId),
            recording,
            organizationId,
            userId,
            role);

        var result = await service.ListAsync(
            statuses,
            reportNumber,
            customerName,
            customerEmail,
            customerAddress,
            search,
            sortBy,
            sortDirection,
            limit,
            offset,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        return Assert.Single(recording.Keys);
    }

    private static Task<Ardalis.Result.Result<JobListResponse>> ListDraftsAsync(JobService service) =>
        ListJobsAsync(service, statuses: [JobStatus.Draft]);

    private static Task<Ardalis.Result.Result<JobListResponse>> ListJobsAsync(
        JobService service,
        List<JobStatus>? statuses = null,
        string? reportNumber = null,
        string? customerName = null,
        string? customerEmail = null,
        string? customerAddress = null,
        string? search = null,
        string? sortBy = null,
        string? sortDirection = null,
        int? limit = 20,
        int? offset = 0) =>
        service.ListAsync(
            statuses,
            reportNumber,
            customerName,
            customerEmail,
            customerAddress,
            search,
            sortBy,
            sortDirection,
            limit,
            offset,
            CancellationToken.None);

    private static JobService CreateService(
        IJobRepository repository,
        HybridCache cache,
        Guid organizationId,
        Guid userId,
        string role = Roles.User) =>
        new(
            repository,
            null!,
            null!,
            null!,
            null!,
            null!,
            cache,
            null!,
            null!,
            null!,
            new TestCurrentUserContext(userId, organizationId, role),
            NullLogger<JobService>.Instance,
            null!,
            null!,
            null!);

    private sealed record TestCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

    /// <summary>
    /// Records every key the service asks for and otherwise behaves exactly like the cache
    /// it wraps, so a test can assert on key names without giving up real cache behaviour.
    /// </summary>
    private sealed class RecordingHybridCache(HybridCache inner) : HybridCache
    {
        private readonly List<string> _keys = [];

        internal IReadOnlyList<string> Keys => _keys;

        public override ValueTask<T> GetOrCreateAsync<TState, T>(
            string key,
            TState state,
            Func<TState, CancellationToken, ValueTask<T>> factory,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default)
        {
            _keys.Add(key);
            return inner.GetOrCreateAsync(key, state, factory, options, tags, cancellationToken);
        }

        public override ValueTask SetAsync<T>(
            string key,
            T value,
            HybridCacheEntryOptions? options = null,
            IEnumerable<string>? tags = null,
            CancellationToken cancellationToken = default)
        {
            _keys.Add(key);
            return inner.SetAsync(key, value, options, tags, cancellationToken);
        }

        public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default) =>
            inner.RemoveAsync(key, cancellationToken);

        public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default) =>
            inner.RemoveByTagAsync(tag, cancellationToken);
    }

    private sealed class UserAwareJobRepository(Guid seenUserId) : IJobRepository
    {
        private readonly Guid jobId = Guid.NewGuid();

        internal int ListCallCount { get; private set; }
        internal List<JobQuery> Queries { get; } = [];

        public Task<JobListResponse> ListAsync(JobQuery query, CancellationToken cancellationToken)
        {
            ListCallCount++;
            Queries.Add(query);
            var item = new JobListItemResponse(
                jobId,
                query.OrganizationId,
                Customer: null,
                ReportNumber: "0001",
                Status: JobStatus.Draft,
                ReportDate: null,
                JobType: JobType.KLS,
                DestinationAddress: null,
                TaskDescription: null,
                InstallationTypes: [],
                AssignedUsers: [],
                SoftDeleted: false,
                TotalHours: null,
                UpdatedAt: DateTimeOffset.UtcNow,
                IsSeenByCurrentUser: query.CurrentUserId == seenUserId,
                IsNewRejection: false);

            return Task.FromResult(new JobListResponse([item], 1));
        }

        public Task<JobReportResponse> CreateAsync(Guid organizationId, CreateJobRequest request, IReadOnlyList<Guid> assignedUserIds, Guid? actorId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<JobReportResponse?> GetSingleJobAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<JobHistoryResponse>?> GetEventsAsync(Guid id, Guid organizationId, int limit, int offset, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<JobReportResponse?> UpdateAsync(Guid id, Guid organizationId, UpdateJobRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<JobTransitionResult?> TransitionAsync(Guid id, Guid organizationId, JobStatus nextStatus, Guid? actorId, string? rejectionNote, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<JobDeleteRepositoryResult> DeleteAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<JobReportResponse?> RestoreDeletionAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> PurgeDeletionScheduledBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
