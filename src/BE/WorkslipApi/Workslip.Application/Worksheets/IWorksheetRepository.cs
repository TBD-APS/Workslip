namespace Workslip.Application.Worksheets;

public interface IWorksheetRepository
{
    Task<WorksheetResponse> UpsertAsync(CreateWorksheetRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(Guid worksheetId, Guid jobId, CancellationToken cancellationToken);
    Task<IReadOnlyList<WorksheetResponse>> ListByJobAsync(Guid jobId, CancellationToken cancellationToken);
}
