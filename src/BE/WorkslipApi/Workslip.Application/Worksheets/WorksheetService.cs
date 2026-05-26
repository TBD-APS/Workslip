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

    public Task<Result<WorksheetResponse>> CreateAsync(CreateWorksheetRequest request, CancellationToken cancellationToken)
    {
        // Implementation intentionally omitted. Structure only.
        throw new NotImplementedException();
    }
}
