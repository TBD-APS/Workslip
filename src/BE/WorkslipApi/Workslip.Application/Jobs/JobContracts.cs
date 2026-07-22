using System.Text.Json.Nodes;
using Workslip.Application.Worksheets;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Application.Jobs;

public sealed record JobQuery(Guid OrganizationId, List<JobStatus>? Statuses, int Limit, int Offset,
    Guid? CurrentUserId = null,
    string? ReportNumber = null,
    string? CustomerName = null, 
    string? CustomerEmail = null,
    string? CustomerAddress = null,
    string? Search = null,
    string? SortBy = null,
    string? SortDirection = null);

public enum JobDeleteRepositoryStatus
{
    Deleted,
    NotFound,
    BlockedByWorksheets
}

public sealed record JobDeleteRepositoryResult(JobDeleteRepositoryStatus Status, int WorksheetCount)
{
    public static JobDeleteRepositoryResult Deleted() => new(JobDeleteRepositoryStatus.Deleted, 0);
    public static JobDeleteRepositoryResult NotFound() => new(JobDeleteRepositoryStatus.NotFound, 0);
    public static JobDeleteRepositoryResult BlockedByWorksheets(int worksheetCount) => new(JobDeleteRepositoryStatus.BlockedByWorksheets, worksheetCount);
}

public sealed record JobDeleteErrorResponse(string Code, string Message, int WorksheetCount)
{
    private const string ConflictSeparator = ":";

    public static JobDeleteErrorResponse HasAttachedWorksheets(int worksheetCount) => new(
        "job_has_attached_worksheets",
        BuildAttachedWorksheetMessage(worksheetCount),
        worksheetCount);

    public string ToConflictError() => string.Join(ConflictSeparator, Code, WorksheetCount);

    public static JobDeleteErrorResponse FromConflictError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return new("job_delete_conflict", "Sagen kunne ikke slettes.", 0);
        }

        var parts = error.Split(ConflictSeparator, 2);
        if (parts[0] == "job_has_attached_worksheets")
        {
            var worksheetCount = parts.Length == 2 && int.TryParse(parts[1], out var parsed)
                ? parsed
                : 1;
            return HasAttachedWorksheets(worksheetCount);
        }

        return new(parts[0], "Sagen kunne ikke slettes.", 0);
    }

    private static string BuildAttachedWorksheetMessage(int worksheetCount)
    {
        var noun = worksheetCount == 1 ? "timeseddel" : "timesedler";
        return $"Sagen kan ikke slettes, fordi den har {worksheetCount} {noun}. Slet {noun} først.";
    }
}

public sealed record ControlCheckRequest(
    string ItemId,
    bool Checked,
    string? Note);

public sealed record ControlSubcategoryRequest(
    string SubcategoryId,
    IReadOnlyList<ControlCheckRequest> ControlChecks);

public sealed record ControlInstallationTypeRequest(
    string InstallationTypeId,
    IReadOnlyList<ControlSubcategoryRequest> Subcategories);

public sealed record ControlCheckResponse(
    Guid Id,
    string ItemId,
    bool Checked,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ControlSubcategoryResponse(
    Guid Id,
    string InstallationTypeId,
    string SubcategoryId,
    IReadOnlyList<ControlCheckResponse> ControlChecks,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ControlInstallationTypeResponse(
    string InstallationTypeId,
    IReadOnlyList<ControlSubcategoryResponse> Subcategories);

public sealed record InstallationTypeControlPointResponse(
    Guid Id,
    string Name,
    int SortOrder,
    bool IsRequired,
    bool IsChecked);

public sealed record InstallationTypeCategoryResponse(
    Guid Id,
    string Name,
    int SortOrder,
    IReadOnlyList<InstallationTypeControlPointResponse> ControlPoints,
    bool IsIrrelevant = false);

public sealed record InstallationTypeResponse(
    Guid Id,
    string Name,
    int SortOrder,
    IReadOnlyList<InstallationTypeCategoryResponse> Categories);

public sealed record CreateInstallationTypeControlPointRequest(
    Guid Id,
    int? SortOrder,
    bool? IsRequired,
    bool? IsChecked = null);

public sealed record CreateInstallationTypeCategoryRequest(
    Guid Id,
    IReadOnlyList<CreateInstallationTypeControlPointRequest>? ControlPoints,
    bool? IsIrrelevant = null);

public sealed record CreateInstallationTypeRequest(
    Guid Id,
    IReadOnlyList<CreateInstallationTypeCategoryRequest>? Categories);

public sealed record CreateJobWorkRequest(
    IReadOnlyList<CreateInstallationTypeRequest>? InstallationTypes,
    string? WorkKind,
    string? CustomWorkKind,
    IReadOnlyList<string>? ClosureFlags,
    string? Remarks,
    string? DestinationAddress = null);

public sealed record CreateJobObservationRequest(
    DateOnly? ReportDate,
    string? TaskDescription,
    string? CustomerObservations,
    string? TechnicalObservations);

public sealed record CreateJobRequest(
    Guid? CustomerId = null,
    CustomerSnapshotData? CustomerSnapshot = null,
    bool? CreateCustomerFromSnapshot = null,
    CreateJobWorkRequest? Work = null,
    CreateJobObservationRequest? Observations = null,
    string? DestinationAddress = null,
    string? DestinationZipCode = null,
    string? DestinationCity = null,
    string? JobType = null,
    IReadOnlyList<CreateTimesheetRequest>? Timesheets = null);

public sealed record CreateTimesheetRequest(
    string WorkDate,
    string UserId,
    decimal HoursWorked,
    bool SleptOnJob);

public sealed record UpdateJobRequest(
    CustomerSnapshotData? CustomerSnapshot = null,
    bool? CreateCustomerFromSnapshot = null,
    CreateJobWorkRequest? Work = null,
    CreateJobObservationRequest? Observations = null,
    string? DestinationAddress = null,
    string? DestinationZipCode = null,
    string? DestinationCity = null,
    string? JobType = null,
    IReadOnlyList<CreateTimesheetRequest>? Timesheets = null);

public sealed record AssignJobRequest(
    IReadOnlyList<Guid> UserIds);

public sealed record ChangeJobStatusRequest(
    JobStatus Status);

public sealed record CustomerInfo(
    Guid? CustomerId,
    string? Name,
    string? Address,
    string? Email,
    string? ContactPerson,
    string? Phone);

public sealed record CustomerSnapshotData(
    string? Name,
    string? Email,
    string? Phone,
    string? Address,
    string? ContactPerson);

public sealed record JobWorkKindResponse(
    Guid Id,
    string NormalizedLabel,
    string Label,
    bool RequiresCustomWorkKind,
    int SortOrder,
    string? CustomWorkKind);

public sealed record JobListResponse(
    IReadOnlyList<JobListItemResponse> Items,
    int TotalCount);

public sealed record JobListItemResponse(
    Guid Id,
    Guid OrganizationId,
    CustomerInfo? Customer,
    string? ReportNumber,
    JobStatus Status,
    DateOnly? ReportDate,
    Workslip.Domain.JobType JobType,
    string? DestinationAddress,
    string? DestinationZipCode,
    string? DestinationCity,
    string? TaskDescription,
    IReadOnlyList<string> InstallationTypes,
    JobWorkKindResponse? WorkKind,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<AssignedUserResponse> AssignedUsers,
    bool SoftDeleted,
    DateTimeOffset? DeletionScheduledAt,
    decimal? TotalHours,
    bool IsSeenByCurrentUser);

public sealed record JobReportResponse(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    string OrganizationCvr,
    CustomerInfo? Customer,
    string? ReportNumber,
    string? DestinationAddress,
    string? DestinationZipCode,
    string? DestinationCity,
    JobStatus Status,
    DateOnly? ReportDate,
    Workslip.Domain.JobType JobType,
    string? TaskDescription,
    string? CustomerObservations,
    string? TechnicalObservations,
    IReadOnlyList<InstallationTypeResponse> InstallationTypes,
    JobWorkKindResponse? WorkKind,
    string? Remarks,
    IReadOnlyList<ClosureFlagResponse> ClosureFlags,
    IReadOnlyList<JobLinkInfoResponse> Links,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt,
    IReadOnlyList<AssignedUserResponse> AssignedUsers,
    IReadOnlyList<WorksheetUserGroupResponse> Worksheets,
    bool SoftDeleted,
    DateTimeOffset? DeletionScheduledAt,
    decimal? TotalHours);

public sealed record CreateJobLinkRequest(
    List<Guid> TargetReportIds);

public sealed record DeleteJobLinksRequest(
    List<Guid> LinkIds);

public sealed record JobLinkInfoResponse(
    Guid Id,
    Guid LinkedReportId,
    string LinkedReportNumber,
    string LinkedCustomerName,
    string LinkedAddress,
    string LinkedStatus);

public sealed record JobLinkResponse(
    Guid Id,
    Guid ReportId,
    Guid LinkedReportId,
    string LinkedReportNumber,
    string LinkedCustomerName,
    string LinkedStatus,
    DateTimeOffset CreatedAt);

public sealed record WorksheetDayEntry(
    DateOnly WorkDate,
    decimal HoursWorked);

public sealed record WorksheetUserGroupResponse(
    string DisplayName,
    decimal TotalHours,
    IReadOnlyList<WorksheetDayEntry> Entries);

public sealed record AssignedUserResponse(
    Guid Id,
    string DisplayName);

public sealed record CustomerSnapshotResponse(
    string? Name,
    string? Email,
    string? Phone,
    string? Address,
    string? ContactPerson);

public sealed record JobReportSummaryResponse(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    string OrganizationCvr,
    string? ReportNumber,
    JobStatus Status,
    Guid? CustomerId,
    CustomerSnapshotResponse CustomerSnapshot,
    string? DestinationAddress,
    string? DestinationZipCode,
    string? DestinationCity,
    string JobType,
    JobReportSummaryWorkResponse Work,
    JobReportSummaryObservationResponse Observations,
    IReadOnlyList<ControlInstallationTypeResponse> ControlInstallationTypes,
    IReadOnlyList<JobLinkInfoResponse> Links,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt,
    IReadOnlyList<AssignedUserResponse> AssignedUsers,
    IReadOnlyList<WorksheetResponse> Worksheets,
    decimal? TotalHours,
    int? TotalOutlay,
    bool SoftDeleted);

public sealed record JobReportSummaryWorkResponse(
    JobWorkKindResponse? WorkKind,
    IReadOnlyList<InstallationTypeResponse> InstallationTypes,
    IReadOnlyList<JobReportSummaryClosureFlagResponse> ClosureFlags,
    string? Remarks);

public sealed record JobReportSummaryClosureFlagResponse(
    Guid Id,
    string NormalizedLabel,
    string Label);

public sealed record JobReportSummaryObservationResponse(
    string? TaskDescription,
    string? CustomerObservations,
    string? TechnicalObservations);

public sealed record JobHistoryResponse(
    Guid Id,
    Guid? ActorId,
    string? ActorName,
    string EventType,
    string? Summary,
    IReadOnlyList<PropertyChange> Changes,
    DateTimeOffset CreatedAt);

public sealed record PropertyChange(
    string PropertyName,
    string? DisplayName,
    string? Before,
    string? After);

public sealed record JobEventResponse(
    Guid Id,
    Guid ReportId,
    Guid? ActorId,
    string EventType,
    string? Summary,
    JsonObject? Before,
    JsonObject? After,
    DateTimeOffset CreatedAt);

