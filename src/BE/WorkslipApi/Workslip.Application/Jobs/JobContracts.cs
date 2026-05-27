using System.Text.Json.Nodes;
using Workslip.Domain;

namespace Workslip.Application.Jobs;

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

public sealed record CreateJobRequest(
    Guid? CustomerId,
    string? ReportNumber,
    string? CustomerName,
    string? CustomerAddress,
    string? CustomerEmail,
    string? ContactPerson,
    string? Phone,
    DateOnly? ReportDate,
    string? TaskDescription,
    string? CustomerObservations,
    string? TechnicalObservations,
    IReadOnlyList<string>? InstallationTypes,
    string? WorkKind,
    string? CustomWorkKind,
    string? Remarks,
    IReadOnlyList<string>? ClosureFlags,
    JsonObject? Payload,
    IReadOnlyList<ControlInstallationTypeRequest>? ControlInstallationTypes);

public sealed record UpdateJobRequest(
    Guid? CustomerId,
    string? ReportNumber,
    string? CustomerName,
    string? CustomerAddress,
    string? CustomerEmail,
    string? ContactPerson,
    string? Phone,
    DateOnly? ReportDate,
    string? TaskDescription,
    string? CustomerObservations,
    string? TechnicalObservations,
    IReadOnlyList<string>? InstallationTypes,
    string? WorkKind,
    string? CustomWorkKind,
    string? Remarks,
    IReadOnlyList<string>? ClosureFlags,
    JsonObject? Payload,
    IReadOnlyList<ControlInstallationTypeRequest>? ControlInstallationTypes);

public sealed record AssignJobRequest(
    IReadOnlyList<Guid>? UserIds);

public sealed record ChangeJobStatusRequest(
    JobStatus Status);

public sealed record JobListItemResponse(
    Guid Id,
    Guid OrganizationId,
    Guid? CustomerId,
    string? ReportNumber,
    JobStatus Status,
    string? CustomerName,
    string? CustomerAddress,
    string? CustomerEmail,
    DateOnly? ReportDate,
    IReadOnlyList<string> InstallationTypes,
    string? WorkKind,
    string? CustomWorkKind,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt,
    IReadOnlyList<AssignedUserResponse> AssignedUsers,
    bool SoftDeleted,
    DateTimeOffset? DeletionScheduledAt);

public sealed record JobReportResponse(
    Guid Id,
    Guid OrganizationId,
    Guid? CustomerId,
    string? ReportNumber,
    JobStatus Status,
    string? CustomerName,
    string? CustomerAddress,
    string? CustomerEmail,
    string? ContactPerson,
    string? Phone,
    DateOnly? ReportDate,
    string? TaskDescription,
    string? CustomerObservations,
    string? TechnicalObservations,
    IReadOnlyList<string> InstallationTypes,
    string? WorkKind,
    string? CustomWorkKind,
    string? Remarks,
    IReadOnlyList<string> ClosureFlags,
    JsonObject? Payload,
    IReadOnlyList<ControlInstallationTypeResponse> ControlInstallationTypes,
    IReadOnlyList<JobLinkInfoResponse> Links,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt,
    IReadOnlyList<AssignedUserResponse> AssignedUsers,
    bool SoftDeleted,
    DateTimeOffset? DeletionScheduledAt);

public sealed record CreateJobLinkRequest(
    Guid TargetReportId,
    string LinkType);

public sealed record JobLinkInfoResponse(
    Guid LinkedReportId,
    string LinkedReportNumber,
    string LinkedCustomerName,
    string LinkedStatus,
    string LinkType);

public sealed record JobLinkResponse(
    Guid Id,
    Guid ReportId,
    Guid LinkedReportId,
    string LinkedReportNumber,
    string LinkedCustomerName,
    string LinkedStatus,
    string LinkType,
    DateTimeOffset CreatedAt);

public sealed record AssignedUserResponse(
    Guid Id,
    string DisplayName);

public sealed record JobReportSummaryResponse(
    Guid Id,
    Guid OrganizationId,
    string? ReportNumber,
    JobStatus Status,
    JobReportSummaryCustomerResponse Customer,
    JobReportSummaryWorkResponse Work,
    JobReportSummaryObservationResponse Observations,
    IReadOnlyList<ControlInstallationTypeResponse> ControlInstallationTypes,
    IReadOnlyList<JobLinkInfoResponse> Links,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt,
    IReadOnlyList<AssignedUserResponse> AssignedUsers,
    bool SoftDeleted,
    DateTimeOffset? DeletionScheduledAt);

public sealed record JobReportSummaryCustomerResponse(
    Guid? CustomerId,
    string? Name,
    string? Address,
    string? Email,
    string? ContactPerson,
    string? Phone);

public sealed record JobReportSummaryWorkResponse(
    string? WorkKind,
    string? WorkKindLabel,
    string? CustomWorkKind,
    IReadOnlyList<string> InstallationTypes,
    IReadOnlyList<JobReportSummaryClosureFlagResponse> ClosureFlags,
    string? Remarks);

public sealed record JobReportSummaryClosureFlagResponse(
    string Id,
    string Label);

public sealed record JobReportSummaryObservationResponse(
    DateOnly? ReportDate,
    string? TaskDescription,
    string? CustomerObservations,
    string? TechnicalObservations,
    JsonObject? Payload);

public sealed record JobEventResponse(
    Guid Id,
    Guid ReportId,
    Guid? ActorId,
    string EventType,
    JsonObject? Before,
    JsonObject? After,
    DateTimeOffset CreatedAt);

