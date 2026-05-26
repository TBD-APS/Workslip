using Ardalis.Result;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Workslip.Application.Worksheets;

public class WorksheetService : IWorksheetService

{
    private readonly IWorksheetRepository _repository;
    private readonly IValidator<CreateWorksheetRequest> _validator;
    private readonly ILogger<WorksheetService> _logger;

    public WorksheetService(IWorksheetRepository repository, IValidator<CreateWorksheetRequest> validator, ILogger<WorksheetService> logger)
    {
        _repository = repository;
        _validator = validator;
        _logger = logger;
    }

    public async Task<Result<WorksheetResponse>> CreateAsync(CreateWorksheetRequest request, CancellationToken cancellationToken)
    {
        var response = await _repository.CreateAsync(request, cancellationToken);

        return response;
    }

    public async Task<Result<WorksheetResponse>> DeleteAsync(string worksheetId, string jobId, CancellationToken cancellationToken)
    {
        var response = await _repository.DeleteAsync(worksheetId, jobId, cancellationToken);
    }
}
