using Ardalis.Result;
using Workslip.Application.Auth;
using Workslip.Application.Images;
using Workslip.Application.Jobs;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Images;

public sealed class ImageServiceAuditorScopeTests
{
    [Fact]
    public async Task ListJobImagesAsync_returns_not_found_before_job_or_storage_access_when_job_is_internal()
    {
        var jobs = new CountingJobRepository();
        var storage = new CountingImageStorage();
        var service = new ImageService(
            storage,
            jobs,
            new HiddenAuditorScopeRepository(),
            new ThrowingUserRepository(),
            new AuditorContext());

        var result = await service.ListJobImagesAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
        Assert.Equal(0, jobs.GetSingleCalls);
        Assert.Equal(0, storage.ListCalls);
    }

    private sealed class AuditorContext : ICurrentUserContext
    {
        public Guid? UserId { get; } = Guid.NewGuid();
        public Guid? OrganizationId { get; } = Guid.NewGuid();
        public string? Role { get; } = Roles.Auditor;
    }

    private sealed class HiddenAuditorScopeRepository : IJobAuditorScopeRepository
    {
        public Task<JobAuditorScopeResponse?> GetAsync(Guid jobId, Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult<JobAuditorScopeResponse?>(new JobAuditorScopeResponse(false, "Intern test-sag"));

        public Task<IReadOnlySet<Guid>> GetVisibleJobIdsAsync(Guid organizationId, IReadOnlyCollection<Guid> jobIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

        public Task<JobAuditorScopeResponse?> SetAsync(Guid jobId, Guid organizationId, bool isInAuditorScope, string? reason, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CountingImageStorage : IImageStorage
    {
        public int ListCalls { get; private set; }

        public Task<IReadOnlyList<ImageInfoResponse>> ListJobImagesAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<ImageInfoResponse>>(Array.Empty<ImageInfoResponse>());
        }

        public Task<ImageInfoResponse> UploadJobImageAsync(Guid organizationId, Guid jobId, Guid imageId, Stream content, string contentType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImageFileResponse?> GetJobImageAsync(Guid organizationId, Guid jobId, Guid imageId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteJobImageAsync(Guid organizationId, Guid jobId, Guid imageId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteJobImagesAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task UploadProfileImageAsync(Guid organizationId, Guid userId, Stream content, string contentType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImageFileResponse?> GetProfileImageAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteProfileImageAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CountingJobRepository : IJobRepository
    {
        public int GetSingleCalls { get; private set; }

        public Task<JobReportResponse?> GetSingleJobAsync(Guid id, Guid organizationId, CancellationToken cancellationToken)
        {
            GetSingleCalls++;
            return Task.FromResult<JobReportResponse?>(null);
        }

        public Task<JobReportResponse> CreateAsync(Guid organizationId, CreateJobRequest request, IReadOnlyList<Guid> assignedUserIds, Guid? actorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobListResponse> ListAsync(JobQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobHistoryResponse>?> GetEventsAsync(Guid id, Guid organizationId, int limit, int offset, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobReportResponse?> UpdateAsync(Guid id, Guid organizationId, UpdateJobRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobTransitionResult?> TransitionAsync(Guid id, Guid organizationId, JobStatus nextStatus, Guid? actorId, string? rejectionNote, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobDeleteRepositoryResult> DeleteAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobReportResponse?> RestoreDeletionAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> PurgeDeletionScheduledBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingUserRepository : IUserRepository
    {
        public Task<UserDataRow?> GetAuthenticatedActorAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UserDataRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UserDataRow?> GetByEmailAsync(string email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UserDataRow?> GetByExternalIdentityAsync(string? entraId, IReadOnlyCollection<string> emailCandidates, CancellationToken cancellationToken) => throw new NotSupportedException();
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
