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
    DateOnly WorkDate,
    decimal HoursWorked,
    bool SleptOnJob,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public interface IWorksheetService
{
    Task<Result<JobReportSummaryResponse>> UpsertAsync(UpsertWorksheetRequest request, CancellationToken cancellationToken);
    Task<Result<JobReportSummaryResponse>> DeleteAsync(Guid worksheetId, Guid jobId, CancellationToken cancellationToken);
}
