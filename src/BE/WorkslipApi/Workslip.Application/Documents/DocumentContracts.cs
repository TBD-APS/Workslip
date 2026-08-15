using Ardalis.Result;

namespace Workslip.Application.Documents;

public sealed record CreateDocumentRequest(
    string Title,
    string Content,
    IReadOnlyList<string>? Tags);

public sealed record UpdateDocumentRequest(
    string Title,
    string Content,
    IReadOnlyList<string>? Tags,
    long Revision);

public sealed record DocumentWriteData(
    string Title,
    string Content,
    IReadOnlyList<string> Tags);

public sealed record DocumentListItemResponse(
    Guid Id,
    string Title,
    string Preview,
    IReadOnlyList<string> Tags,
    DateTimeOffset UpdatedAt,
    string? UpdatedByDisplayName,
    long Revision);

public sealed record DocumentListResponse(
    IReadOnlyList<DocumentListItemResponse> Items,
    int TotalCount);

public sealed record DocumentDetailResponse(
    Guid Id,
    string Title,
    string Content,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? CreatedByUserId,
    string? CreatedByDisplayName,
    Guid? UpdatedByUserId,
    string? UpdatedByDisplayName,
    long Revision);

public sealed record DocumentAttachmentInfoResponse(
    Guid Id,
    Guid DocumentId,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    Guid? UploadedByUserId,
    string? UploadedByDisplayName);

public sealed record DocumentAttachmentUpload(
    Stream Content,
    long ContentLength,
    string FileName,
    string ContentType);

public sealed record DocumentAttachmentFileResponse(
    Stream Content,
    long ContentLength,
    string FileName,
    string ContentType);

public sealed record DocumentAttachmentStoredFile(
    Stream Content,
    long ContentLength);

public sealed class DocumentRevisionConflictException(Guid documentId)
    : Exception($"Document '{documentId}' was updated by another request.");
