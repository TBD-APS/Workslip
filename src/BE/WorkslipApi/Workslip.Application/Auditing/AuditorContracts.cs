using Ardalis.Result;
using Workslip.Application.Images;

namespace Workslip.Application.Auditing;

public interface IAuditorService
{
    Task<Result<IReadOnlyList<AuditorOrganizationSummaryResponse>>> ListOrganizationsAsync(CancellationToken cancellationToken);
    Task<Result<AuditorReportListResponse>> ListReportsAsync(Guid organizationId, string? search, string? installationType, int? limit, int? offset, CancellationToken cancellationToken);
    Task<Result<AuditorReportDetailResponse>> GetReportAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<ImageInfoResponse>>> ListImagesAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken);
    Task<Result<ImageFileResponse>> GetImageAsync(Guid organizationId, Guid jobId, Guid imageId, CancellationToken cancellationToken);
    Task<Result<AuditorFindingResponse>> CreateFindingAsync(Guid organizationId, Guid jobId, CreateAuditorFindingRequest request, CancellationToken cancellationToken);
    Task<Result<AuditorFindingResponse>> UpdateFindingAsync(Guid organizationId, Guid jobId, Guid findingId, UpdateAuditorFindingRequest request, CancellationToken cancellationToken);
    Task<Result<AuditorFindingResponse>> AddCompanyEvidenceAsync(Guid findingId, AddAuditorFindingEvidenceRequest request, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<AuditorAssignmentAdminResponse>>> ListAssignmentsAsync(CancellationToken cancellationToken);
    Task<Result<AuditorAssignmentAdminResponse>> CreateAssignmentAsync(CreateAuditorAssignmentRequest request, CancellationToken cancellationToken);
    Task<Result<AuditorAssignmentAdminResponse>> UpdateAssignmentAsync(Guid assignmentId, UpdateAuditorAssignmentRequest request, CancellationToken cancellationToken);
}

public interface IAuditorRepository
{
    Task<IReadOnlyList<AuditorOrganizationSummaryResponse>> ListOrganizationsAsync(Guid auditorUserId, Guid controlOrganizationId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<AuditorAssignmentGrant?> GetActiveAssignmentAsync(Guid auditorUserId, Guid controlOrganizationId, Guid targetOrganizationId, DateTimeOffset now, CancellationToken cancellationToken);
    Task<bool> IsVisibleJobAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken);
    Task<AuditorReportListResponse> ListReportsAsync(Guid organizationId, string? search, string? installationType, int limit, int offset, CancellationToken cancellationToken);
    Task<AuditorReportDetailResponse?> GetReportAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditorFindingResponse>> ListFindingsAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditorEventResponse>> ListEventsAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken);
    Task RecordReportOpenedAsync(Guid assignmentId, Guid organizationId, Guid jobId, Guid actorUserId, CancellationToken cancellationToken);
    Task<AuditorFindingResponse?> CreateFindingAsync(Guid assignmentId, Guid organizationId, Guid jobId, Guid actorUserId, CreateAuditorFindingCommand command, CancellationToken cancellationToken);
    Task<bool> FindingBelongsToJobAsync(Guid findingId, Guid organizationId, Guid jobId, CancellationToken cancellationToken);
    Task<AuditorFindingResponse?> UpdateFindingAsync(Guid assignmentId, Guid organizationId, Guid jobId, Guid findingId, Guid actorUserId, UpdateAuditorFindingCommand command, CancellationToken cancellationToken);
    Task<AuditorFindingTarget?> GetFindingTargetAsync(Guid findingId, Guid organizationId, CancellationToken cancellationToken);
    Task<AuditorFindingResponse?> AddCompanyEvidenceAsync(AuditorFindingTarget target, Guid findingId, Guid actorUserId, string evidence, DateTimeOffset now, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuditorAssignmentAdminResponse>> ListAssignmentsAsync(CancellationToken cancellationToken);
    Task<AuditorUserSubject?> GetAuditorUserAsync(Guid auditorUserId, CancellationToken cancellationToken);
    Task<bool> OrganizationExistsAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<bool> ActiveAssignmentExistsAsync(Guid auditorUserId, Guid targetOrganizationId, string authorizationArea, DateTimeOffset now, CancellationToken cancellationToken);
    Task<AuditorAssignmentAdminResponse?> CreateAssignmentAsync(Guid actorUserId, AuditorUserSubject auditor, CreateAuditorAssignmentCommand command, CancellationToken cancellationToken);
    Task<AuditorAssignmentTarget?> GetAssignmentTargetAsync(Guid assignmentId, CancellationToken cancellationToken);
    Task<AuditorAssignmentAdminResponse?> UpdateAssignmentAsync(Guid assignmentId, Guid actorUserId, bool isActive, DateTimeOffset? activeUntil, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record AuditorOrganizationSummaryResponse(Guid AssignmentId, Guid OrganizationId, string OrganizationName, string Cvr, string AuthorizationArea, DateTimeOffset ActiveFrom, DateTimeOffset? ActiveUntil, int OpenFindings, DateTimeOffset? LastActivityAt);
public sealed record AuditorReportListResponse(IReadOnlyList<AuditorReportListItemResponse> Items, int TotalCount);
public sealed record AuditorReportListItemResponse(Guid Id, string? ReportNumber, string? CustomerName, string? Address, string Status, string? InstallationTypes, decimal? TotalHours, string? AssignedUsers, string? SubmittedBy, DateTime? ReportDate, DateTimeOffset? SubmittedAt, DateTimeOffset UpdatedAt, int OpenFindings);

public sealed record AuditorReportDetailResponse(
    Guid Id,
    string OrganizationName,
    string OrganizationCvr,
    string? ReportNumber,
    string? CustomerName,
    string? CustomerEmail,
    string? CustomerPhone,
    string? CustomerAddress,
    string? CustomerContactPerson,
    string? DestinationAddress,
    string? DestinationZipCode,
    string? DestinationCity,
    string Status,
    string JobType,
    DateTime? ReportDate,
    string? TaskDescription,
    string? CustomerObservations,
    string? TechnicalObservations,
    string? Remarks,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SubmittedAt,
    string? SubmittedBy,
    IReadOnlyList<AuditorInstallationResponse> Installations,
    IReadOnlyList<AuditorPersonResponse> AssignedUsers,
    IReadOnlyList<AuditorWorksheetResponse> Worksheets,
    IReadOnlyList<AuditorFindingResponse> Findings,
    IReadOnlyList<AuditorEventResponse> AuditEvents,
    IReadOnlyList<ImageInfoResponse> Images);

public sealed record AuditorInstallationResponse(Guid Id, string Name, IReadOnlyList<AuditorControlCategoryResponse> Categories);
public sealed record AuditorControlCategoryResponse(Guid Id, string Name, bool IsIrrelevant, IReadOnlyList<AuditorControlPointResponse> ControlPoints);
public sealed record AuditorControlPointResponse(Guid Id, string Name, bool IsRequired, bool IsChecked);
public sealed record AuditorPersonResponse(Guid Id, string DisplayName, string? Email);
public sealed record AuditorWorksheetResponse(Guid Id, DateTime WorkDate, decimal HoursWorked, Guid UserId, string UserName);
public sealed record AuditorFindingResponse(Guid Id, Guid AssignmentId, string Category, string Description, string? Reference, DateTimeOffset? DueAt, string Status, string? CompanyEvidence, DateTimeOffset? CompanyEvidenceAt, string? CreatedBy, string? CompanyEvidenceBy, string? VerifiedBy, DateTimeOffset? VerifiedAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record AuditorEventResponse(Guid Id, string EventType, string? DetailsJson, DateTimeOffset CreatedAt, string? ActorName);

public sealed record CreateAuditorFindingRequest(string Category, string Description, string? Reference, DateTimeOffset? DueAt);
public sealed record UpdateAuditorFindingRequest(string Status, string? Description, DateTimeOffset? DueAt);
public sealed record AddAuditorFindingEvidenceRequest(string Evidence);
public sealed record CreateAuditorAssignmentRequest(Guid AuditorUserId, Guid TargetOrganizationId, string AuthorizationArea, DateTimeOffset? ActiveFrom, DateTimeOffset? ActiveUntil);
public sealed record UpdateAuditorAssignmentRequest(bool IsActive, DateTimeOffset? ActiveUntil);

public sealed record AuditorAssignmentAdminResponse(Guid Id, Guid AuditorUserId, string AuditorName, string AuditorEmail, Guid ControlOrganizationId, string ControlOrganizationName, Guid TargetOrganizationId, string TargetOrganizationName, string TargetOrganizationCvr, string AuthorizationArea, DateTimeOffset ActiveFrom, DateTimeOffset? ActiveUntil, bool IsActive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record AuditorAssignmentGrant(Guid Id, Guid AuditorUserId, Guid ControlOrganizationId, Guid TargetOrganizationId, string AuthorizationArea, DateTimeOffset ActiveFrom, DateTimeOffset? ActiveUntil);
public sealed record AuditorUserSubject(Guid Id, Guid OrganizationId, string DisplayName, string Email, string Role);
public sealed record AuditorAssignmentTarget(Guid TargetOrganizationId, DateTimeOffset ActiveFrom);
public sealed record AuditorFindingTarget(Guid AssignmentId, Guid TargetOrganizationId, Guid JobReportId, string Status);
public sealed record CreateAuditorFindingCommand(string Category, string Description, string? Reference, DateTimeOffset? DueAt, DateTimeOffset Now);
public sealed record UpdateAuditorFindingCommand(string Status, string? Description, DateTimeOffset? DueAt, DateTimeOffset Now);
public sealed record CreateAuditorAssignmentCommand(Guid AuditorUserId, Guid TargetOrganizationId, string AuthorizationArea, DateTimeOffset ActiveFrom, DateTimeOffset? ActiveUntil, DateTimeOffset Now);
