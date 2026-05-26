namespace Workslip.Application.Worksheets;

public interface IWorksheetRepository
{
    Task<Workslip.Contracts.Worksheets.WorksheetResponse> CreateAsync(Workslip.Contracts.Worksheets.CreateWorksheetRequest request, CancellationToken cancellationToken);
}
