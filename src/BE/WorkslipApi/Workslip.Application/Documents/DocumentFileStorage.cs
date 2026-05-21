namespace Workslip.Documents;

public sealed record StoredDocumentFile(
    string StorageAccountName,
    string ContainerName,
    string BlobName,
    string? BlobVersionId,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string Sha256Hash);

public sealed record StoredDocumentFileContent(
    Stream Content,
    string FileName,
    string ContentType);

public interface IDocumentFileStorage
{
    Task<StoredDocumentFile> SaveAsync(
        Guid reportId,
        Stream content,
        string fileName,
        string? contentType,
        CancellationToken cancellationToken);

    Task<StoredDocumentFileContent?> OpenReadAsync(DocumentFileResponse file, CancellationToken cancellationToken);
}
