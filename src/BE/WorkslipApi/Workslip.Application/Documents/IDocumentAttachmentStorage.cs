namespace Workslip.Application.Documents;

public interface IDocumentAttachmentStorage
{
    Task UploadAsync(
        Guid organizationId,
        Guid documentId,
        Guid attachmentId,
        Stream content,
        string contentType,
        CancellationToken cancellationToken);

    Task<DocumentAttachmentStoredFile?> GetAsync(
        Guid organizationId,
        Guid documentId,
        Guid attachmentId,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        Guid organizationId,
        Guid documentId,
        Guid attachmentId,
        CancellationToken cancellationToken);

    Task DeleteDocumentAsync(
        Guid organizationId,
        Guid documentId,
        CancellationToken cancellationToken);
}
