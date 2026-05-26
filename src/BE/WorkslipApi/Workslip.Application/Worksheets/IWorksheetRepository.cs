namespace Workslip.Application.Worksheets;

public interface IWorksheetRepository
{
    Task<WorksheetResponse> CreateAsync(CreateWorksheetRequest request, CancellationToken cancellationToken);
}
