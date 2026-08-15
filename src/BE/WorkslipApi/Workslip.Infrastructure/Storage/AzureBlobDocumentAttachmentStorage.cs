using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Workslip.Application.Documents;

namespace Workslip.Infrastructure.Storage;

public sealed class AzureBlobDocumentAttachmentStorage : IDocumentAttachmentStorage
{
    private const string DefaultContainerName = "uploads";
    private readonly BlobContainerClient _container;

    public AzureBlobDocumentAttachmentStorage(IConfiguration configuration, TokenCredential credential)
    {
        var accountName = configuration["Azure:DocumentFileStorage:StorageAccountName"];
        if (string.IsNullOrWhiteSpace(accountName))
            throw new InvalidOperationException("Azure:DocumentFileStorage:StorageAccountName is required for document attachment storage.");

        var containerName = configuration["Azure:DocumentFileStorage:ContainerName"];
        if (string.IsNullOrWhiteSpace(containerName))
            containerName = DefaultContainerName;

        _container = new BlobContainerClient(
            new Uri($"https://{accountName}.blob.core.windows.net/{containerName}"),
            credential);
    }

    public async Task UploadAsync(
        Guid organizationId,
        Guid documentId,
        Guid attachmentId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(AttachmentName(organizationId, documentId, attachmentId));
        await blob.UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            },
            cancellationToken);
    }

    public async Task<DocumentAttachmentStoredFile?> GetAsync(
        Guid organizationId,
        Guid documentId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var blob = _container.GetBlobClient(AttachmentName(organizationId, documentId, attachmentId));
        try
        {
            var download = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return new DocumentAttachmentStoredFile(
                download.Value.Content,
                download.Value.Details.ContentLength);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(
        Guid organizationId,
        Guid documentId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        await _container.DeleteBlobIfExistsAsync(
            AttachmentName(organizationId, documentId, attachmentId),
            cancellationToken: cancellationToken);
    }

    public async Task DeleteDocumentAsync(
        Guid organizationId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var prefix = AttachmentPrefix(organizationId, documentId);
        await foreach (var blob in _container.GetBlobsAsync(
                           BlobTraits.None,
                           BlobStates.None,
                           prefix,
                           cancellationToken))
        {
            await _container.DeleteBlobIfExistsAsync(blob.Name, cancellationToken: cancellationToken);
        }
    }

    private static string AttachmentPrefix(Guid organizationId, Guid documentId) =>
        $"organizations/{organizationId:N}/docs/{documentId:N}/attachments/";

    private static string AttachmentName(Guid organizationId, Guid documentId, Guid attachmentId) =>
        $"{AttachmentPrefix(organizationId, documentId)}{attachmentId:N}";
}
