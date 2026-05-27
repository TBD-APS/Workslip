using Ardalis.Result;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;

namespace Workslip.Application.Worksheets;

public class WorksheetService : IWorksheetService

{
    private readonly IWorksheetRepository _repository;
    private readonly IValidator<CreateWorksheetRequest> _validator;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<WorksheetService> _logger;

    public WorksheetService(IWorksheetRepository repository, IValidator<CreateWorksheetRequest> validator, ICurrentUserContext currentUserContext, ILogger<WorksheetService> logger)
    {
        _repository = repository;
        _validator = validator;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public async Task<Result<WorksheetResponse>> CreateAsync(CreateWorksheetRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await _validator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .Select(e => new ValidationError
                {
                    Identifier = e.PropertyName,
                    ErrorMessage = e.ErrorMessage
                })
                .ToList();

            _logger.LogWarning("Worksheet create validation failed. Fields: {Fields}",
                string.Join(", ", errors.Select(e => e.Identifier).Distinct()));

            return Result<WorksheetResponse>.Invalid(errors);
        }

        try
        {
            var response = await _repository.CreateAsync(request, cancellationToken);
            return Result<WorksheetResponse>.Success(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Worksheet creation failed due to business rule violation");
            return Result<WorksheetResponse>.Conflict(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during worksheet creation");
            return Result<WorksheetResponse>.Error(ex.Message);
        }
    }

    public async Task<Result<WorksheetResponse>> DeleteAsync(Guid worksheetId, Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            await _repository.DeleteAsync(worksheetId, jobId, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during worksheet deletion");
            return Result<WorksheetResponse>.Error(ex.Message);
        }
    }
}
