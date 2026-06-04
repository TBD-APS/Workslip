using System.Text.Json.Nodes;
using Workslip.Application.Worksheets;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Application.Jobs;

public sealed record JobQuery(Guid OrganizationId, JobStatus? Status, int Limit, int Offset,
    string? ReportNumber = null,
    string? CustomerName = null, 
    string? CustomerEmail = null,
    string? CustomerAddress = null);

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
    string? Description,
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
    string? Description,
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
    string? Remarks);

public sealed record CreateJobObservationRequest(
    DateOnly? ReportDate,
    string? TaskDescription,
    string? CustomerObservations,
    string? TechnicalObservations);

public sealed record CreateJobRequest(
    CustomerInfo? Customer,
    string? ReportNumber,
    CreateJobWorkRequest? Work,
    CreateJobObservationRequest? Observations);

public sealed record UpdateJobRequest(
    CustomerInfo? Customer,
    string? ReportNumber,
    CreateJobWorkRequest? Work,
    CreateJobObservationRequest? Observations);

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

public sealed record JobWorkKindResponse(
    Guid Id,
    string NormalizedLabel,
    string Label,
    bool RequiresCustomWorkKind,
    int SortOrder,
    string? CustomWorkKind);

public sealed record JobListItemResponse(
    Guid Id,
    Guid OrganizationId,
    CustomerInfo? Customer,
    string? ReportNumber,
    JobStatus Status,
    DateOnly? ReportDate,
    IReadOnlyList<string> InstallationTypes,
    JobWorkKindResponse? WorkKind,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt,
    IReadOnlyList<AssignedUserResponse> AssignedUsers,
    bool SoftDeleted,
    DateTimeOffset? DeletionScheduledAt,
    decimal? TotalHours);

public sealed record JobReportResponse(
    Guid Id,
    Guid OrganizationId,
    CustomerInfo? Customer,
    string? ReportNumber,
    JobStatus Status,
    DateOnly? ReportDate,
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

public sealed record JobReportSummaryResponse(
    Guid Id,
    Guid OrganizationId,
    string? ReportNumber,
    JobStatus Status,
    CustomerInfo Customer,
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
    DateOnly? ReportDate,
    string? TaskDescription,
    string? CustomerObservations,
    string? TechnicalObservations);

public sealed record JobEventResponse(
    Guid Id,
    Guid ReportId,
    Guid? ActorId,
    string EventType,
    JsonObject? Before,
    JsonObject? After,
    DateTimeOffset CreatedAt);

