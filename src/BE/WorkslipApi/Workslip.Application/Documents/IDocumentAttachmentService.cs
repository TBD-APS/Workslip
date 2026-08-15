using Ardalis.Result;

namespace Workslip.Application.Documents;

public interface IDocumentAttachmentService
{
    Task<Result<IReadOnlyList<DocumentAttachmentInfoResponse>>> ListAsync(
        Guid documentId,
        CancellationToken cancellationToken);

    Task<Result<DocumentAttachmentInfoResponse>> UploadAsync(
        Guid documentId,
        DocumentAttachmentUpload upload,
        CancellationToken cancellationToken);

    Task<Result<DocumentAttachmentFileResponse>> GetAsync(
        Guid documentId,
        Guid attachmentId,
        CancellationToken cancellationToken);

    Task<Result> DeleteAsync(
        Guid documentId,
        Guid attachmentId,
        CancellationToken cancellationToken);
}
