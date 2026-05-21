using System.Text.Json.Nodes;
using Workslip.Domain;

namespace Workslip.Jobs;

public sealed record ControlCheckRequest(
    string StageId,
    string ColumnId,
    string ItemId,
    bool Checked,
    string? Note);

public sealed record ControlCheckResponse(
    Guid Id,
    string StageId,
    string ColumnId,
    string ItemId,
    bool Checked,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CreateJobRequest(
    Guid OrganizationId,
    string ReportNumber,
    string CustomerName,
    string CustomerAddress,
    string? ContactPerson,
    string? Phone,
    DateOnly? ReportDate,
    string TaskDescription,
    string? CustomerObservations,
    IReadOnlyList<string> InstallationTypes,
    string WorkKind,
    string? CustomWorkKind,
    string? Remarks,
    IReadOnlyList<string> ClosureFlags,
    JsonObject? Payload,
    IReadOnlyList<ControlCheckRequest> ControlChecks);

public sealed record UpdateJobRequest(
    string? ReportNumber,
    string? CustomerName,
    string? CustomerAddress,
    string? ContactPerson,
    string? Phone,
    DateOnly? ReportDate,
    string? TaskDescription,
    string? CustomerObservations,
    IReadOnlyList<string>? InstallationTypes,
    string? WorkKind,
    string? CustomWorkKind,
    string? Remarks,
    IReadOnlyList<string>? ClosureFlags,
    JsonObject? Payload,
    IReadOnlyList<ControlCheckRequest>? ControlChecks);

public sealed record JobListItemResponse(
    Guid Id,
    Guid OrganizationId,
    string ReportNumber,
    JobStatus Status,
    string CustomerName,
    string CustomerAddress,
    DateOnly? ReportDate,
    IReadOnlyList<string> InstallationTypes,
    string WorkKind,
    string? CustomWorkKind,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt);

public sealed record JobReportResponse(
    Guid Id,
    Guid OrganizationId,
    string ReportNumber,
    JobStatus Status,
    string CustomerName,
    string CustomerAddress,
    string? ContactPerson,
    string? Phone,
    DateOnly? ReportDate,
    string TaskDescription,
    string? CustomerObservations,
    IReadOnlyList<string> InstallationTypes,
    string WorkKind,
    string? CustomWorkKind,
    string? Remarks,
    IReadOnlyList<string> ClosureFlags,
    JsonObject? Payload,
    IReadOnlyList<ControlCheckResponse> ControlChecks,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt);

public sealed record JobEventResponse(
    Guid Id,
    Guid ReportId,
    Guid? ActorId,
    string EventType,
    JsonObject? Before,
    JsonObject? After,
    DateTimeOffset CreatedAt);

public sealed record JobValidationError(string Field, string Message);
