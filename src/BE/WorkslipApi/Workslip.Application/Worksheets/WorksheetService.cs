using Ardalis.Result;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;

namespace Workslip.Application.Worksheets;

public class WorksheetService : IWorksheetService

{
    private readonly IWorksheetRepository _repository;
    private readonly IJobService _jobService;
    private readonly IValidator<UpsertWorksheetRequest> _validator;
    private readonly ICurrentUserContext _currentUserContext;
    private readonly ILogger<WorksheetService> _logger;

    public WorksheetService(
        IWorksheetRepository repository,
        IJobService jobService,
        IValidator<UpsertWorksheetRequest> validator,
        ICurrentUserContext currentUserContext,
        ILogger<WorksheetService> logger)
    {
        _repository = repository;
        _jobService = jobService;
        _validator = validator;
        _currentUserContext = currentUserContext;
        _logger = logger;
    }

    public async Task<Result<JobReportSummaryResponse>> UpsertAsync(UpsertWorksheetRequest request, CancellationToken cancellationToken)
    {
        var organizationId = _currentUserContext.OrganizationId;
        if (organizationId is null)
        {
            return Result<JobReportSummaryResponse>.Unauthorized();
        }

        var validationResult = await _validator.ValidateAsync(request, cancellationToken);

        if (!validationResult.IsValid)
        {
            var errors = MapValidationErrors(validationResult);
            _logger.LogWarning("Worksheet upsert validation failed. Fields: {Fields}",
                string.Join(", ", errors.Select(e => e.Identifier).Distinct()));

            return Result<JobReportSummaryResponse>.Invalid(errors);
        }

        try
        {
            await _repository.UpsertAsync(request, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Worksheet upsert failed due to business rule violation");
            return Result<JobReportSummaryResponse>.Conflict(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during worksheet upsert");
            return Result<JobReportSummaryResponse>.Error(ex.Message);
        }

        return await _jobService.GetSingleJobAsync(request.JobId, cancellationToken);
    }

    public async Task<Result<JobReportSummaryResponse>> DeleteAsync(Guid worksheetId, Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            var organizationId = _currentUserContext.OrganizationId;
            if (organizationId is null)
            {
                return Result<JobReportSummaryResponse>.Unauthorized();
            }

            await _repository.DeleteAsync(worksheetId, jobId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during worksheet deletion");
            return Result<JobReportSummaryResponse>.Error(ex.Message);
        }

        return await _jobService.GetSingleJobAsync(jobId, cancellationToken);
    }

    private static List<ValidationError> MapValidationErrors(ValidationResult result) =>
        result.Errors
            .Select(e => new ValidationError { Identifier = e.PropertyName, ErrorMessage = e.ErrorMessage })
            .ToList();
}
