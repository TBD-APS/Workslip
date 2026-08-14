using Ardalis.Result;

namespace Workslip.Application.Images;

public interface IImageService
{
    Task<Result<IReadOnlyList<ImageInfoResponse>>> ListJobImagesAsync(
        Guid jobId,
        CancellationToken cancellationToken);

    Task<Result<ImageInfoResponse>> UploadJobImageAsync(
        Guid jobId,
        ImageUpload upload,
        CancellationToken cancellationToken);

    Task<Result<ImageFileResponse>> GetJobImageAsync(
        Guid jobId,
        Guid imageId,
        CancellationToken cancellationToken);

    Task<Result> DeleteJobImageAsync(
        Guid jobId,
        Guid imageId,
        CancellationToken cancellationToken);

    Task<Result> UploadCurrentProfileImageAsync(
        ImageUpload upload,
        CancellationToken cancellationToken);

    Task<Result<ImageFileResponse>> GetProfileImageAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<Result> DeleteCurrentProfileImageAsync(
        CancellationToken cancellationToken);
}
