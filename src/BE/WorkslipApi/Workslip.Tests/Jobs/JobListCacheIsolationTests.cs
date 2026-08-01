using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Domain;
using Xunit;

namespace Workslip.Tests.Jobs;

public sealed class JobListCacheIsolationTests
{
    [Fact]
    public async Task ListAsync_DoesNotReuseSeenStateAcrossUsers()
    {
        var organizationId = Guid.NewGuid();
        var seenUserId = Guid.NewGuid();
        var unseenUserId = Guid.NewGuid();
        var repository = new UserAwareJobRepository(organizationId, seenUserId);

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
    }

    [Fact]
    public async Task ListAsync_ReusesCacheForTheSameUser()
    {
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var repository = new UserAwareJobRepository(organizationId, userId);

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

    private static ServiceProvider CreateCacheServices()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        return services.BuildServiceProvider();
    }

    private static Task<Ardalis.Result.Result<JobListResponse>> ListDraftsAsync(JobService service) =>
        service.ListAsync(
            [JobStatus.Draft],
            reportNumber: null,
            customerName: null,
            customerEmail: null,
            customerAddress: null,
            search: null,
            sortBy: null,
            sortDirection: null,
            limit: 20,
            offset: 0,
            CancellationToken.None);

    private static JobService CreateService(
        IJobRepository repository,
        HybridCache cache,
        Guid organizationId,
        Guid userId) =>
        new(
            repository,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            cache,
            null!,
            null!,
            null!,
            new TestCurrentUserContext(userId, organizationId, Roles.User),
            NullLogger<JobService>.Instance,
            null!,
            null!,
            null!);

    private sealed record TestCurrentUserContext(
        Guid? UserId,
        Guid? OrganizationId,
        string? Role) : ICurrentUserContext;

    private sealed class UserAwareJobRepository(Guid organizationId, Guid seenUserId) : IJobRepository
    {
        private readonly Guid jobId = Guid.NewGuid();

        internal int ListCallCount { get; private set; }

        public Task<JobListResponse> ListAsync(JobQuery query, CancellationToken cancellationToken)
        {
            ListCallCount++;
            var item = new JobListItemResponse(
                jobId,
                organizationId,
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
