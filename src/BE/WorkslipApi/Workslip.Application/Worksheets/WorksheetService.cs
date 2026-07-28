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

    public async Task<Result<MyWorksheetsMonthResponse>> GetWorksheetsForUserAsync(int? year, int? month, CancellationToken cancellationToken)
    {
        var userId = _currentUserContext.UserId;
        var organizationId = _currentUserContext.OrganizationId;

        if (userId is null || organizationId is null)
        {
            return Result<MyWorksheetsMonthResponse>.Unauthorized();
        }

        var now = DateTimeOffset.UtcNow;
        var selectedYear = year ?? now.Year;
        var selectedMonth = month ?? now.Month;

        if (selectedYear is < 2000 or > 2100 || selectedMonth is < 1 or > 12)
        {
            return Result<MyWorksheetsMonthResponse>.Invalid([new ValidationError
            {
                Identifier = "month",
                ErrorMessage = "Vælg en gyldig måned."
            }]);
        }

        var monthStart = new DateOnly(selectedYear, selectedMonth, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var entries = await _repository.GetWorksheetsForUserAsync(userId.Value, organizationId.Value, monthStart, monthEnd, cancellationToken);

        return Result<MyWorksheetsMonthResponse>.Success(BuildMonthResponse(selectedYear, selectedMonth, monthStart, monthEnd, entries));
    }

    public async Task<Result<MyWorksheetsMonthResponse>> GetAllWorksheetsAsync(int? year, int? month, CancellationToken cancellationToken)
    {
        var organizationId = _currentUserContext.OrganizationId;

        if (organizationId is null)
        {
            return Result<MyWorksheetsMonthResponse>.Unauthorized();
        }

        var (selectedYear, selectedMonth) = ResolveYearMonth(year, month);
        if (selectedYear is null)
        {
            return Result<MyWorksheetsMonthResponse>.Invalid([new ValidationError
            {
                Identifier = "month",
                ErrorMessage = "Vælg en gyldig måned."
            }]);
        }

        var (monthStart, monthEnd) = GetMonthRange(selectedYear.Value, selectedMonth!.Value);
        var entries = await _repository.GetAllWorksheetsAsync(organizationId.Value, monthStart, monthEnd, cancellationToken);

        return Result<MyWorksheetsMonthResponse>.Success(BuildMonthResponse(selectedYear.Value, selectedMonth.Value, monthStart, monthEnd, entries));
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
            _logger.LogWarning(ex, "Worksheet upsert failed due to business rule violation. JobId: {JobId}", request.JobId);
            return Result<JobReportSummaryResponse>.Conflict("worksheet_rule_violation");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during worksheet upsert. JobId: {JobId}", request.JobId);
            return Result<JobReportSummaryResponse>.Error("worksheet_unexpected_error");
        }

        await _jobService.InvalidateJobDetailCacheAsync(request.JobId, organizationId.Value, cancellationToken);

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
            _logger.LogError(ex, "Unexpected error during worksheet deletion. WorksheetId: {WorksheetId}, JobId: {JobId}", worksheetId, jobId);
            return Result<JobReportSummaryResponse>.Error("worksheet_unexpected_error");
        }

        if (_currentUserContext.OrganizationId.HasValue)
            await _jobService.InvalidateJobDetailCacheAsync(jobId, _currentUserContext.OrganizationId.Value, cancellationToken);

        return await _jobService.GetSingleJobAsync(jobId, cancellationToken);
    }

    private static MyWorksheetsMonthResponse BuildMonthResponse(
        int year,
        int month,
        DateOnly monthStart,
        DateOnly monthEnd,
        IReadOnlyList<MyWorksheetEntryResponse> entries)
    {
        var entriesByDate = entries
            .GroupBy(e => e.WorkDate)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.CustomerName).ThenBy(e => e.ReportNumber).ToArray());

        var weekStart = StartOfWeek(monthStart);
        var lastWeekStart = StartOfWeek(monthEnd);
        var weeks = new List<MyWorksheetWeekResponse>();

        for (var start = weekStart; start <= lastWeekStart; start = start.AddDays(7))
        {
            var days = Enumerable.Range(0, 7)
                .Select(offset => BuildDayResponse(start.AddDays(offset), entriesByDate))
                .ToArray();

            weeks.Add(new MyWorksheetWeekResponse(
                start,
                start.AddDays(6),
                days.Sum(d => d.TotalHours),
                days.Sum(d => d.OutlayCount),
                days));
        }

        return new MyWorksheetsMonthResponse(
            year,
            month,
            monthStart,
            monthEnd,
            weeks.Sum(w => w.TotalHours),
            weeks.Sum(w => w.OutlayCount),
            weeks);
    }

    private static MyWorksheetDayResponse BuildDayResponse(
        DateOnly date,
        IReadOnlyDictionary<DateOnly, MyWorksheetEntryResponse[]> entriesByDate)
    {
        var entries = entriesByDate.GetValueOrDefault(date) ?? [];
        return new MyWorksheetDayResponse(
            date,
            entries.Sum(e => e.HoursWorked),
            entries.Count(e => e.HasOutlay),
            entries);
    }

    private static DateOnly StartOfWeek(DateOnly date)
    {
        var daysFromMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-daysFromMonday);
    }

    private static (int? year, int? month) ResolveYearMonth(int? year, int? month)
    {
        var now = DateTimeOffset.UtcNow;
        var selectedYear = year ?? now.Year;
        var selectedMonth = month ?? now.Month;
        if (selectedYear is < 2000 or > 2100 || selectedMonth is < 1 or > 12)
            return (null, null);
        return (selectedYear, selectedMonth);
    }

    private static (DateOnly monthStart, DateOnly monthEnd) GetMonthRange(int year, int month)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        return (monthStart, monthEnd);
    }

    private static List<ValidationError> MapValidationErrors(ValidationResult result) =>
        result.Errors
            .Select(e => new ValidationError { Identifier = e.PropertyName, ErrorMessage = e.ErrorMessage })
            .ToList();
}
