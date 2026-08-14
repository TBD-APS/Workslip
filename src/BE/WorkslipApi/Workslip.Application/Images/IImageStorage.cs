namespace Workslip.Application.Images;

public interface IImageStorage
{
    Task<IReadOnlyList<ImageInfoResponse>> ListJobImagesAsync(
        Guid organizationId,
        Guid jobId,
        CancellationToken cancellationToken);

    Task<ImageInfoResponse> UploadJobImageAsync(
        Guid organizationId,
        Guid jobId,
        Guid imageId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken);

    Task<ImageFileResponse?> GetJobImageAsync(
        Guid organizationId,
        Guid jobId,
        Guid imageId,
        CancellationToken cancellationToken);

    Task DeleteJobImageAsync(
        Guid organizationId,
        Guid jobId,
        Guid imageId,
        CancellationToken cancellationToken);

    Task DeleteJobImagesAsync(
        Guid organizationId,
        Guid jobId,
        CancellationToken cancellationToken);

    Task UploadProfileImageAsync(
        Guid organizationId,
        Guid userId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken);

    Task<ImageFileResponse?> GetProfileImageAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken);

    Task DeleteProfileImageAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken);
}
