using Ardalis.Result;
using Workslip.Application.Jobs;

namespace Workslip.Application.Worksheets;

/// <summary>
/// Request contract for creating a worksheet.
/// </summary>
public sealed record UpsertWorksheetRequest(
    Guid? Id,
    Guid JobId,
    Guid UserId,
    string UserDisplayName,
    DateOnly WorkDate,
    decimal HoursWorked,
    bool SleptOnJob);


/// <summary>
/// Response contract representing a worksheet.
/// </summary>
public sealed record WorksheetResponse(
    Guid Id,
    Guid OrganizationId,
    Guid JobId,
    Guid UserId,
    string UserDisplayName,
    DateOnly WorkDate,
    decimal HoursWorked,
    bool SleptOnJob,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record MyWorksheetEntryResponse(
    DateOnly WorkDate,
    Guid JobId,
    string? ReportNumber,
    string CustomerName,
    string? CustomerAddress,
    decimal HoursWorked,
    bool HasOutlay);

public sealed record MyWorksheetDayResponse(
    DateOnly Date,
    decimal TotalHours,
    int OutlayCount,
    IReadOnlyList<MyWorksheetEntryResponse> Entries);

public sealed record MyWorksheetWeekResponse(
    DateOnly WeekStart,
    DateOnly WeekEnd,
    decimal TotalHours,
    int OutlayCount,
    IReadOnlyList<MyWorksheetDayResponse> Days);

public sealed record MyWorksheetsMonthResponse(
    int Year,
    int Month,
    DateOnly MonthStart,
    DateOnly MonthEnd,
    decimal TotalHours,
    int OutlayCount,
    IReadOnlyList<MyWorksheetWeekResponse> Weeks);

public interface IWorksheetService
{
    Task<Result<JobReportSummaryResponse>> UpsertAsync(UpsertWorksheetRequest request, CancellationToken cancellationToken);
    Task<Result<JobReportSummaryResponse>> DeleteAsync(Guid worksheetId, Guid jobId, CancellationToken cancellationToken);
    Task<Result<MyWorksheetsMonthResponse>> GetWorksheetsForUserAsync(int? year, int? month, CancellationToken cancellationToken);
}
