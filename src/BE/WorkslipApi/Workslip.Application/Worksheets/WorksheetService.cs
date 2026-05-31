using Ardalis.Result;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;

namespace Workslip.Application.Worksheets;

public class WorksheetService : IWorksheetService

{
    private readonly IWorksheetRepository _repository;
    private readonly IValidator<CreateWorksheetRequest> _validator;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<WorksheetService> _logger;

    public WorksheetService(IWorksheetRepository repository, 
        IValidator<CreateWorksheetRequest> validator, ICurrentUserContext currentUserContext, ILogger<WorksheetService> logger)
    {
        _repository = repository;
        _validator = validator;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public async Task<Result<WorksheetResponse>> UpsertAsync(CreateWorksheetRequest request, CancellationToken cancellationToken)
    {
        var organizationId = _currentUserContext.OrganizationId;
        if (organizationId is null)
        {
            return Result<WorksheetResponse>.Unauthorized();
        }

        var validationResult = await _validator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
        {
            var errors = MapValidationErrors(validationResult);
            _logger.LogWarning("Worksheet upsert validation failed. Fields: {Fields}",
                string.Join(", ", errors.Select(e => e.Identifier).Distinct()));

            return Result<WorksheetResponse>.Invalid(errors);
        }

        try
        {
            var response = await _repository.UpsertAsync(request, cancellationToken);
            return Result<WorksheetResponse>.Success(response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Worksheet upsert failed due to business rule violation");
            return Result<WorksheetResponse>.Conflict(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during worksheet upsert");
            return Result<WorksheetResponse>.Error(ex.Message);
        }
    }

    public async Task<Result<IReadOnlyList<WorksheetResponse>>> ListByJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            var organizationId = _currentUserContext.OrganizationId;
            if (organizationId is null)
            {
                return Result<IReadOnlyList<WorksheetResponse>>.Unauthorized();
            }

            var worksheets = await _repository.ListByJobAsync(jobId, cancellationToken);
            return Result<IReadOnlyList<WorksheetResponse>>.Success(worksheets);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error listing worksheets for job {JobId}", jobId);
            return Result<IReadOnlyList<WorksheetResponse>>.Error(ex.Message);
        }
    }

    public async Task<Result<WorksheetResponse>> DeleteAsync(Guid worksheetId, Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            var organizationId = _currentUserContext.OrganizationId;
            if (organizationId is null)
            {
                return Result<WorksheetResponse>.Unauthorized();
            }

            await _repository.DeleteAsync(worksheetId, jobId, cancellationToken);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during worksheet deletion");
            return Result<WorksheetResponse>.Error(ex.Message);
        }
    }

    private static List<ValidationError> MapValidationErrors(ValidationResult result) =>
        result.Errors
            .Select(e => new ValidationError { Identifier = e.PropertyName, ErrorMessage = e.ErrorMessage })
            .ToList();
}
