using Ardalis.Result;
using Workslip.Application.Auth;
using Workslip.Application.Images;
using Workslip.Application.Jobs;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Xunit;

namespace Workslip.Tests.Images;

public sealed class ImageServiceValidationTests
{
    [Fact]
    public async Task UploadCurrentProfileImageAsync_AcceptsJpegWhenMimeAndSignatureMatch()
    {
        var storage = new CapturingImageStorage();
        var service = CreateService(storage);
        await using var content = new MemoryStream([0xFF, 0xD8, 0xFF, 0x00, 0x01]);

        var result = await service.UploadCurrentProfileImageAsync(
            new ImageUpload(content, content.Length, "image/jpeg"),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Ok, result.Status);
        Assert.Equal(1, storage.ProfileUploadCalls);
        Assert.Equal("image/jpeg", storage.LastProfileContentType);
    }

    [Fact]
    public async Task UploadCurrentProfileImageAsync_RejectsMimeSignatureMismatchBeforeStorage()
    {
        var storage = new CapturingImageStorage();
        var service = CreateService(storage);
        await using var content = new MemoryStream([0xFF, 0xD8, 0xFF, 0x00, 0x01]);

        var result = await service.UploadCurrentProfileImageAsync(
            new ImageUpload(content, content.Length, "image/png"),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(0, storage.ProfileUploadCalls);
        Assert.Contains(result.ValidationErrors, error =>
            error.Identifier == "file" && error.ErrorMessage.Contains("matcher ikke", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UploadCurrentProfileImageAsync_RejectsUnsupportedContentTypeBeforeReadingStorage()
    {
        var storage = new CapturingImageStorage();
        var service = CreateService(storage);
        await using var content = new MemoryStream("<svg></svg>"u8.ToArray());

        var result = await service.UploadCurrentProfileImageAsync(
            new ImageUpload(content, content.Length, "image/svg+xml"),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        Assert.Equal(0, storage.ProfileUploadCalls);
    }

    private static ImageService CreateService(CapturingImageStorage storage) =>
        new(
            storage,
            new ThrowingJobRepository(),
            new ThrowingUserRepository(),
            new FakeCurrentUserContext());

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public Guid? UserId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
        public Guid? OrganizationId { get; } = Guid.Parse("22222222-2222-2222-2222-222222222222");
        public string? Role { get; } = Roles.User;
    }

    private sealed class CapturingImageStorage : IImageStorage
    {
        public int ProfileUploadCalls { get; private set; }
        public string? LastProfileContentType { get; private set; }

        public Task UploadProfileImageAsync(Guid organizationId, Guid userId, Stream content, string contentType, CancellationToken cancellationToken)
        {
            ProfileUploadCalls++;
            LastProfileContentType = contentType;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ImageInfoResponse>> ListJobImagesAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImageInfoResponse> UploadJobImageAsync(Guid organizationId, Guid jobId, Guid imageId, Stream content, string contentType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImageFileResponse?> GetJobImageAsync(Guid organizationId, Guid jobId, Guid imageId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteJobImageAsync(Guid organizationId, Guid jobId, Guid imageId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteJobImagesAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ImageFileResponse?> GetProfileImageAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteProfileImageAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ThrowingJobRepository : IJobRepository
    {
        public Task<JobReportResponse> CreateAsync(Guid organizationId, CreateJobRequest request, IReadOnlyList<Guid> assignedUserIds, Guid? actorId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobListResponse> ListAsync(JobQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<JobReportResponse?> GetSingleJobAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) => throw new NotSupportedException();
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
