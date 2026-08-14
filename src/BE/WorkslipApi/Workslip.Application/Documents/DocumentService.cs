using Ardalis.Result;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;

namespace Workslip.Application.Documents;

public sealed class DocumentService(
    IDocumentRepository documentRepository,
    IDocumentAttachmentStorage attachmentStorage,
    ICurrentUserContext currentUser,
    IValidator<CreateDocumentRequest> createValidator,
    IValidator<UpdateDocumentRequest> updateValidator,
    ILogger<DocumentService> logger) : IDocumentService
{
    private const int MaxSearchLength = 120;

    public async Task<Result<DocumentListResponse>> ListAsync(
        int? limit,
        int? offset,
        string? search,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var organizationId))
            return Result<DocumentListResponse>.Unauthorized();

        var normalizedLimit = Math.Clamp(limit ?? 50, 1, 100);
        var normalizedOffset = Math.Max(offset ?? 0, 0);
        var normalizedSearch = NormalizeSearch(search);

        var documents = await documentRepository.ListAsync(
            organizationId,
            normalizedLimit,
            normalizedOffset,
            normalizedSearch,
            cancellationToken);
        var totalCount = await documentRepository.CountAsync(organizationId, normalizedSearch, cancellationToken);

        return Result<DocumentListResponse>.Success(new DocumentListResponse(documents, totalCount));
    }

    public async Task<Result<DocumentDetailResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var organizationId))
            return Result<DocumentDetailResponse>.Unauthorized();

        var document = await documentRepository.GetByIdAsync(organizationId, id, cancellationToken);
        return document is null
            ? Result<DocumentDetailResponse>.NotFound()
            : Result<DocumentDetailResponse>.Success(document);
    }

    public async Task<Result<DocumentDetailResponse>> CreateAsync(
        CreateDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var organizationId))
            return Result<DocumentDetailResponse>.Unauthorized();

        var normalized = Normalize(request);
        var validation = await createValidator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
            return Result<DocumentDetailResponse>.Invalid(ToValidationErrors(validation.Errors));

        logger.LogInformation("Creating internal document in org {OrgId}", organizationId);
        var created = await documentRepository.CreateAsync(
            organizationId,
            currentUser.UserId,
            new DocumentWriteData(normalized.Title, normalized.Content, normalized.Tags ?? []),
            cancellationToken);

        return Result<DocumentDetailResponse>.Success(created);
    }

    public async Task<Result<DocumentDetailResponse>> UpdateAsync(
        Guid id,
        UpdateDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var organizationId))
            return Result<DocumentDetailResponse>.Unauthorized();

        var normalized = Normalize(request);
        var validation = await updateValidator.ValidateAsync(normalized, cancellationToken);
        if (!validation.IsValid)
            return Result<DocumentDetailResponse>.Invalid(ToValidationErrors(validation.Errors));

        try
        {
            var updated = await documentRepository.UpdateAsync(
                organizationId,
                id,
                currentUser.UserId,
                new DocumentWriteData(normalized.Title, normalized.Content, normalized.Tags ?? []),
                normalized.Revision,
                cancellationToken);

            if (updated is null)
                return Result<DocumentDetailResponse>.NotFound();

            logger.LogInformation("Updated internal document {DocumentId} in org {OrgId}", id, organizationId);
            return Result<DocumentDetailResponse>.Success(updated);
        }
        catch (DocumentRevisionConflictException)
        {
            logger.LogWarning("Internal document revision conflict. DocumentId: {DocumentId}. OrgId: {OrgId}", id, organizationId);
            return Result<DocumentDetailResponse>.Conflict("document_revision_conflict");
        }
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetOrganizationId(out var organizationId))
            return Result.Unauthorized();

        var deleted = await documentRepository.DeleteAsync(organizationId, id, cancellationToken);
        if (!deleted)
            return Result.NotFound();

        try
        {
            await attachmentStorage.DeleteDocumentAsync(organizationId, id, cancellationToken);
        }
        catch (Exception exception)
        {
            // The SQL delete is authoritative and cascades attachment metadata.
            // Remaining blob objects are unreachable and can be cleaned up
            // operationally without resurrecting the deleted document.
            logger.LogWarning(
                exception,
                "Failed to clean up attachment blobs for deleted internal document {DocumentId} in org {OrgId}",
                id,
                organizationId);
        }

        logger.LogInformation("Deleted internal document {DocumentId} in org {OrgId}", id, organizationId);
        return Result.Success();
    }

    private bool TryGetOrganizationId(out Guid organizationId)
    {
        if (currentUser.OrganizationId is Guid id && id != Guid.Empty)
        {
            organizationId = id;
            return true;
        }

        logger.LogWarning("Internal document operation requested without OrganizationId in claims.");
        organizationId = Guid.Empty;
        return false;
    }

    private static CreateDocumentRequest Normalize(CreateDocumentRequest request) =>
        request with
        {
            Title = request.Title?.Trim() ?? string.Empty,
            Content = request.Content ?? string.Empty,
            Tags = NormalizeTags(request.Tags)
        };

    private static UpdateDocumentRequest Normalize(UpdateDocumentRequest request) =>
        request with
        {
            Title = request.Title?.Trim() ?? string.Empty,
            Content = request.Content ?? string.Empty,
            Tags = NormalizeTags(request.Tags)
        };

    private static IReadOnlyList<string> NormalizeTags(IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count == 0)
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<string>(tags.Count);
        foreach (var tag in tags)
        {
            var value = tag?.Trim() ?? string.Empty;
            if (value.Length == 0 || !seen.Add(value))
                continue;
            normalized.Add(value);
        }

        return normalized;
    }

    private static string? NormalizeSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return null;

        var normalized = search.Trim();
        return normalized.Length <= MaxSearchLength
            ? normalized
            : normalized[..MaxSearchLength];
    }

    private static List<ValidationError> ToValidationErrors(IEnumerable<FluentValidation.Results.ValidationFailure> errors) =>
        errors.Select(error => new ValidationError
        {
            Identifier = error.PropertyName,
            ErrorMessage = error.ErrorMessage
        }).ToList();
}
