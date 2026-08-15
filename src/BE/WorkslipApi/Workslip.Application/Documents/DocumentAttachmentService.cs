using Ardalis.Result;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;

namespace Workslip.Application.Documents;

public sealed class DocumentAttachmentService(
    IDocumentRepository documentRepository,
    IDocumentAttachmentRepository attachmentRepository,
    IDocumentAttachmentStorage storage,
    ICurrentUserContext currentUser,
    ILogger<DocumentAttachmentService> logger) : IDocumentAttachmentService
{
    public const long MaxAttachmentSizeBytes = 20 * 1024 * 1024;
    private const int MaxFileNameLength = 180;

    private static readonly IReadOnlyDictionary<string, string[]> AllowedContentTypes =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [".mp3"] = ["audio/mpeg", "audio/mp3"],
            [".wav"] = ["audio/wav", "audio/x-wav", "audio/wave"],
            [".ogg"] = ["audio/ogg"],
            [".mp4"] = ["video/mp4"],
            [".pdf"] = ["application/pdf"],
            [".png"] = ["image/png"],
            [".jpg"] = ["image/jpeg"],
            [".jpeg"] = ["image/jpeg"],
            [".webp"] = ["image/webp"],
            [".txt"] = ["text/plain"],
            [".md"] = ["text/markdown", "text/plain"],
            [".csv"] = ["text/csv", "application/csv", "text/plain"]
        };

    public async Task<Result<IReadOnlyList<DocumentAttachmentInfoResponse>>> ListAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var organizationId))
            return Result<IReadOnlyList<DocumentAttachmentInfoResponse>>.Unauthorized();

        if (await documentRepository.GetByIdAsync(organizationId, documentId, cancellationToken) is null)
            return Result<IReadOnlyList<DocumentAttachmentInfoResponse>>.NotFound();

        var attachments = await attachmentRepository.ListAsync(organizationId, documentId, cancellationToken);
        return Result<IReadOnlyList<DocumentAttachmentInfoResponse>>.Success(attachments);
    }

    public async Task<Result<DocumentAttachmentInfoResponse>> UploadAsync(
        Guid documentId,
        DocumentAttachmentUpload upload,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var organizationId))
            return Result<DocumentAttachmentInfoResponse>.Unauthorized();

        if (await documentRepository.GetByIdAsync(organizationId, documentId, cancellationToken) is null)
            return Result<DocumentAttachmentInfoResponse>.NotFound();

        var validation = ValidateUpload(upload);
        if (validation is not null)
            return Result<DocumentAttachmentInfoResponse>.Invalid(validation);

        var fileName = Path.GetFileName(upload.FileName.Trim());
        var contentType = NormalizeContentType(upload.ContentType);
        var attachmentId = Guid.NewGuid();

        await storage.UploadAsync(
            organizationId,
            documentId,
            attachmentId,
            upload.Content,
            contentType,
            cancellationToken);

        try
        {
            var created = await attachmentRepository.CreateAsync(
                organizationId,
                documentId,
                attachmentId,
                fileName,
                contentType,
                upload.ContentLength,
                currentUser.UserId,
                cancellationToken);

            logger.LogInformation(
                "Uploaded internal document attachment {AttachmentId} to document {DocumentId} in org {OrgId}",
                attachmentId,
                documentId,
                organizationId);
            return Result<DocumentAttachmentInfoResponse>.Success(created);
        }
        catch
        {
            try
            {
                await storage.DeleteAsync(
                    organizationId,
                    documentId,
                    attachmentId,
                    CancellationToken.None);
            }
            catch (Exception cleanupException)
            {
                logger.LogWarning(
                    cleanupException,
                    "Failed to clean up unreferenced document attachment {AttachmentId} in org {OrgId}",
                    attachmentId,
                    organizationId);
            }

            throw;
        }
    }

    public async Task<Result<DocumentAttachmentFileResponse>> GetAsync(
        Guid documentId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var organizationId))
            return Result<DocumentAttachmentFileResponse>.Unauthorized();

        var metadata = await attachmentRepository.GetAsync(
            organizationId,
            documentId,
            attachmentId,
            cancellationToken);
        if (metadata is null)
            return Result<DocumentAttachmentFileResponse>.NotFound();

        var storedFile = await storage.GetAsync(
            organizationId,
            documentId,
            attachmentId,
            cancellationToken);
        if (storedFile is null)
        {
            logger.LogWarning(
                "Document attachment blob missing. AttachmentId: {AttachmentId}. DocumentId: {DocumentId}. OrgId: {OrgId}",
                attachmentId,
                documentId,
                organizationId);
            return Result<DocumentAttachmentFileResponse>.NotFound();
        }

        return Result<DocumentAttachmentFileResponse>.Success(new DocumentAttachmentFileResponse(
            storedFile.Content,
            storedFile.ContentLength,
            metadata.FileName,
            metadata.ContentType));
    }

    public async Task<Result> DeleteAsync(
        Guid documentId,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var organizationId))
            return Result.Unauthorized();

        var deleted = await attachmentRepository.DeleteAsync(
            organizationId,
            documentId,
            attachmentId,
            cancellationToken);
        if (!deleted)
            return Result.NotFound();

        try
        {
            await storage.DeleteAsync(
                organizationId,
                documentId,
                attachmentId,
                cancellationToken);
        }
        catch (Exception exception)
        {
            // Metadata is already gone, so the object is no longer reachable
            // through Workslip. Keep the user-visible delete successful and
            // retain IDs in logs for operational orphan cleanup.
            logger.LogWarning(
                exception,
                "Failed to delete orphaned document attachment blob {AttachmentId}. DocumentId: {DocumentId}. OrgId: {OrgId}",
                attachmentId,
                documentId,
                organizationId);
        }

        logger.LogInformation(
            "Deleted internal document attachment {AttachmentId} from document {DocumentId} in org {OrgId}",
            attachmentId,
            documentId,
            organizationId);
        return Result.Success();
    }

    private bool TryGetOrganizationId(out Guid organizationId)
    {
        if (currentUser.OrganizationId is Guid id && id != Guid.Empty)
        {
            organizationId = id;
            return true;
        }

        logger.LogWarning("Document attachment operation requested without OrganizationId in claims.");
        organizationId = Guid.Empty;
        return false;
    }

    private static List<ValidationError>? ValidateUpload(DocumentAttachmentUpload upload)
    {
        if (upload.Content is null || !upload.Content.CanRead)
            return [Error("Content", "Filen kunne ikke læses.")];

        if (upload.ContentLength <= 0)
            return [Error("ContentLength", "Filen er tom.")];

        if (upload.ContentLength > MaxAttachmentSizeBytes)
            return [Error("ContentLength", "Filen må højst være 20 MB.")];

        var fileName = Path.GetFileName(upload.FileName?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(fileName))
            return [Error("FileName", "Filnavn er påkrævet.")];

        if (fileName.Length > MaxFileNameLength)
            return [Error("FileName", $"Filnavn må højst være {MaxFileNameLength} tegn.")];

        if (fileName.Any(char.IsControl))
            return [Error("FileName", "Filnavnet indeholder ugyldige tegn.")];

        var extension = Path.GetExtension(fileName);
        var contentType = NormalizeContentType(upload.ContentType);
        if (!AllowedContentTypes.TryGetValue(extension, out var allowed)
            || !allowed.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            return [Error(
                "ContentType",
                "Filtypen er ikke tilladt. Brug MP3/WAV/OGG, MP4, PDF, PNG/JPG/WebP, TXT/MD eller CSV.")];
        }

        return null;
    }

    private static string NormalizeContentType(string? contentType) =>
        (contentType ?? string.Empty).Split(';', 2)[0].Trim().ToLowerInvariant();

    private static ValidationError Error(string identifier, string message) => new()
    {
        Identifier = identifier,
        ErrorMessage = message
    };
}
