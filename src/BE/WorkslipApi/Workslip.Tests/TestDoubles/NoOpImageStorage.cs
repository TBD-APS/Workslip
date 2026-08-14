using Workslip.Application.Images;

namespace Workslip.Tests.TestDoubles;

internal sealed class NoOpImageStorage : IImageStorage
{
    public Task<IReadOnlyList<ImageInfoResponse>> ListJobImagesAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ImageInfoResponse>>(Array.Empty<ImageInfoResponse>());

    public Task<ImageInfoResponse> UploadJobImageAsync(Guid organizationId, Guid jobId, Guid imageId, Stream content, string contentType, CancellationToken cancellationToken) =>
        Task.FromResult(new ImageInfoResponse(imageId, contentType, content.Length, DateTimeOffset.UtcNow));

    public Task<ImageFileResponse?> GetJobImageAsync(Guid organizationId, Guid jobId, Guid imageId, CancellationToken cancellationToken) =>
        Task.FromResult<ImageFileResponse?>(null);

    public Task DeleteJobImageAsync(Guid organizationId, Guid jobId, Guid imageId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task DeleteJobImagesAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task UploadProfileImageAsync(Guid organizationId, Guid userId, Stream content, string contentType, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<ImageFileResponse?> GetProfileImageAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<ImageFileResponse?>(null);

    public Task DeleteProfileImageAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
