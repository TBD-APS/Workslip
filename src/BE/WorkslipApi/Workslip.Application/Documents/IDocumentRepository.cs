namespace Workslip.Application.Documents;

public interface IDocumentRepository
{
    Task<DocumentListResponse> ListAsync(
        Guid organizationId,
        int limit,
        int offset,
        string? search,
        CancellationToken cancellationToken);

    Task<bool> ExistsAsync(
        Guid organizationId,
        Guid id,
        CancellationToken cancellationToken);

    Task<DocumentDetailResponse?> GetByIdAsync(
        Guid organizationId,
        Guid id,
        CancellationToken cancellationToken);

    Task<DocumentDetailResponse> CreateAsync(
        Guid organizationId,
        Guid? actorUserId,
        DocumentWriteData document,
        CancellationToken cancellationToken);

    Task<DocumentDetailResponse?> UpdateAsync(
        Guid organizationId,
        Guid id,
        Guid? actorUserId,
        DocumentWriteData document,
        long expectedRevision,
        CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid organizationId, Guid id, CancellationToken cancellationToken);
}
