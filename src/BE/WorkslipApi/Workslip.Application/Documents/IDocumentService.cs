using Ardalis.Result;

namespace Workslip.Application.Documents;

public interface IDocumentService
{
    Task<Result<DocumentListResponse>> ListAsync(
        int? limit,
        int? offset,
        string? search,
        CancellationToken cancellationToken);

    Task<Result<DocumentDetailResponse>> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Result<DocumentDetailResponse>> CreateAsync(
        CreateDocumentRequest request,
        CancellationToken cancellationToken);

    Task<Result<DocumentDetailResponse>> UpdateAsync(
        Guid id,
        UpdateDocumentRequest request,
        CancellationToken cancellationToken);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken);
}
