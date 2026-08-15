namespace Workslip.Application.Documents;

public interface IDocumentAttachmentRepository
{
    Task<IReadOnlyList<DocumentAttachmentInfoResponse>> ListAsync(
        Guid organizationId,
        Guid documentId,
        CancellationToken cancellationToken);

    Task<DocumentAttachmentInfoResponse?> GetAsync(
        Guid organizationId,
        Guid documentId,
        Guid attachmentId,
        CancellationToken cancellationToken);

    Task<DocumentAttachmentInfoResponse> CreateAsync(
        Guid organizationId,
        Guid documentId,
        Guid attachmentId,
        string fileName,
        string contentType,
        long sizeBytes,
        Guid? actorUserId,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(
        Guid organizationId,
        Guid documentId,
        Guid attachmentId,
        CancellationToken cancellationToken);
}
