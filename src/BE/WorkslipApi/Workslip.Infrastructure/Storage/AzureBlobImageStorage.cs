using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Workslip.Application.Images;

namespace Workslip.Infrastructure.Storage;

public sealed class AzureBlobImageStorage : IImageStorage
{
    private const string DefaultContainerName = "uploads";
    private readonly BlobContainerClient _container;

    public AzureBlobImageStorage(IConfiguration configuration, TokenCredential credential)
    {
        var accountName = configuration["Azure:DocumentFileStorage:StorageAccountName"];
        if (string.IsNullOrWhiteSpace(accountName))
        {
            throw new InvalidOperationException("Azure:DocumentFileStorage:StorageAccountName is required for image storage.");
        }

        var containerName = configuration["Azure:DocumentFileStorage:ContainerName"];
        if (string.IsNullOrWhiteSpace(containerName))
        {
            containerName = DefaultContainerName;
        }

        _container = new BlobContainerClient(
            new Uri($"https://{accountName}.blob.core.windows.net/{containerName}"),
            credential);
    }

    public async Task<IReadOnlyList<ImageInfoResponse>> ListJobImagesAsync(
        Guid organizationId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var prefix = JobImagePrefix(organizationId, jobId);
        var images = new List<ImageInfoResponse>();

        await foreach (var blob in _container.GetBlobsAsync(
                           BlobTraits.None,
                           BlobStates.None,
                           prefix,
                           cancellationToken))
        {
            var idText = blob.Name[prefix.Length..];
            if (!Guid.TryParseExact(idText, "N", out var imageId))
            {
                continue;
            }

            var contentType = blob.Properties.ContentType;
            var contentLength = blob.Properties.ContentLength;
            if (string.IsNullOrWhiteSpace(contentType) || contentLength is null)
            {
                continue;
            }

            images.Add(new ImageInfoResponse(
                imageId,
                contentType,
                contentLength.Value,
                blob.Properties.CreatedOn ?? blob.Properties.LastModified ?? DateTimeOffset.MinValue));
        }

        return images
            .OrderBy(image => image.CreatedAt)
            .ToArray();
    }

    public async Task<ImageInfoResponse> UploadJobImageAsync(
        Guid organizationId,
        Guid jobId,
        Guid imageId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(JobImageName(organizationId, jobId, imageId));
        var uploaded = await blob.UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            },
            cancellationToken);

        return new ImageInfoResponse(
            imageId,
            contentType,
            content.Length,
            uploaded.Value.LastModified);
    }

    public Task<ImageFileResponse?> GetJobImageAsync(
        Guid organizationId,
        Guid jobId,
        Guid imageId,
        CancellationToken cancellationToken) =>
        GetFileAsync(JobImageName(organizationId, jobId, imageId), cancellationToken);

    public async Task DeleteJobImageAsync(
        Guid organizationId,
        Guid jobId,
        Guid imageId,
        CancellationToken cancellationToken)
    {
        await _container.DeleteBlobIfExistsAsync(
            JobImageName(organizationId, jobId, imageId),
            cancellationToken: cancellationToken);
    }

    public async Task DeleteJobImagesAsync(
        Guid organizationId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var prefix = JobImagePrefix(organizationId, jobId);
        await foreach (var blob in _container.GetBlobsAsync(
                           BlobTraits.None,
                           BlobStates.None,
                           prefix,
                           cancellationToken))
        {
            await _container.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
        }
    }

    public async Task UploadProfileImageAsync(
        Guid organizationId,
        Guid userId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(ProfileImageName(organizationId, userId));
        await blob.UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            },
            cancellationToken);
    }

    public Task<ImageFileResponse?> GetProfileImageAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken) =>
        GetFileAsync(ProfileImageName(organizationId, userId), cancellationToken);

    public async Task DeleteProfileImageAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await _container.DeleteBlobIfExistsAsync(
            ProfileImageName(organizationId, userId),
            cancellationToken: cancellationToken);
    }

    private async Task<ImageFileResponse?> GetFileAsync(
        string blobName,
        CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(blobName);

        try
        {
            var download = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
            var details = download.Value.Details;
            return new ImageFileResponse(
                download.Value.Content,
                details.ContentType ?? "application/octet-stream",
                details.ContentLength);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    private static string JobImagePrefix(Guid organizationId, Guid jobId) =>
        $"organizations/{organizationId:N}/jobs/{jobId:N}/images/";

    private static string JobImageName(Guid organizationId, Guid jobId, Guid imageId) =>
        $"{JobImagePrefix(organizationId, jobId)}{imageId:N}";

    private static string ProfileImageName(Guid organizationId, Guid userId) =>
        $"organizations/{organizationId:N}/users/{userId:N}/profile";
}
