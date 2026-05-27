using Ardalis.Result;

namespace Workslip.Application.Worksheets;

/// <summary>
/// Request contract for creating a worksheet.
/// </summary>
public sealed record CreateWorksheetRequest(
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
    Task<Result<WorksheetResponse>> UpsertAsync(CreateWorksheetRequest request, CancellationToken cancellationToken);
    Task<Result<WorksheetResponse>> DeleteAsync(Guid worksheetId, Guid jobId, CancellationToken cancellationToken);
}
