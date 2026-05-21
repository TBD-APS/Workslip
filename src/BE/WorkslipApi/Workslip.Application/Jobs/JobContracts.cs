using System.Text.Json.Nodes;
using Workslip.Domain;

namespace Workslip.Application.Jobs;

public sealed record ControlCheckRequest(
    string ItemId,
    bool Checked,
    string? Note);

public sealed record ControlSubcategoryRequest(
    string SubcategoryId,
    bool IsIrrelevant,
    string? Note,
    IReadOnlyList<ControlCheckRequest> ControlChecks);

public sealed record ControlCategoryRequest(
    string CategoryId,
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
    string CategoryId,
    string SubcategoryId,
    bool IsIrrelevant,
    string? Note,
    IReadOnlyList<ControlCheckResponse> ControlChecks,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ControlCategoryResponse(
    string CategoryId,
    IReadOnlyList<ControlSubcategoryResponse> Subcategories);

public sealed record CreateJobRequest(
    Guid OrganizationId,
    Guid? CustomerId,
    string ReportNumber,
    string CustomerName,
    string CustomerAddress,
    string? CustomerEmail,
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
    IReadOnlyList<ControlCategoryRequest> ControlCategories);

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
    IReadOnlyList<string>? InstallationTypes,
    string? WorkKind,
    string? CustomWorkKind,
    string? Remarks,
    IReadOnlyList<string>? ClosureFlags,
    JsonObject? Payload,
    IReadOnlyList<ControlCategoryRequest>? ControlCategories);

public sealed record JobListItemResponse(
    Guid Id,
    Guid OrganizationId,
    Guid? CustomerId,
    string ReportNumber,
    JobStatus Status,
    string CustomerName,
    string CustomerAddress,
    string? CustomerEmail,
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
    Guid? CustomerId,
    string ReportNumber,
    JobStatus Status,
    string CustomerName,
    string CustomerAddress,
    string? CustomerEmail,
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
    IReadOnlyList<ControlCategoryResponse> ControlCategories,
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
