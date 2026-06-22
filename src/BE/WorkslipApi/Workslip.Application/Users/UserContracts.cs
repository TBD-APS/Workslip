namespace Workslip.Application.Users;

public sealed record CreateUserRequest(
    string Email,
    string DisplayName,
    string Phone,
    string Role);

public sealed record UpdateUserRequest(
    string? DisplayName,
    string? Phone,
    string? Role);

public sealed record UserResponse(
    Guid Id,
    Guid OrganizationId,
    string Email,
    string DisplayName,
    string Phone,
    string Role);

public sealed record UserListResponse(
    IReadOnlyList<UserResponse> Users,
    int Total);

public sealed record AssignedJobResponse(
    Guid ReportId,
    string? ReportNumber,
    string Status,
    DateTimeOffset UpdatedAt,
    string? CustomerName,
    string? CustomerEmail,
    string? CustomerAddress);

public sealed record UserDetailResponse(
    Guid Id,
    Guid OrganizationId,
    string Email,
    string DisplayName,
    string Phone,
    string Role,
    IReadOnlyList<AssignedJobResponse> AssignedJobs,
    decimal? TotalHours);

public sealed record InviteUsersRequest(
    IReadOnlyList<string> Emails,
    string InviteBaseUrl,
    string? Role);

public sealed record InviteUserResult(
    string Email,
    bool Success,
    string? Error,
    string? InviteLink);

public sealed record InviteUsersResponse(
    IReadOnlyList<InviteUserResult> Results);

public sealed record InviteTokenResponse(
    Guid Id,
    string Email,
    string? Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    bool Consumed,
    DateTimeOffset? OpenedAt,
    DateTimeOffset? AcceptedAt,
    string? EntraUserId,
    bool EntraCreatedByInvite,
    DateTimeOffset? EntraProvisionedAt,
    DateTimeOffset? EntraCleanedAt);

public sealed record InviteListResponse(
    IReadOnlyList<InviteTokenResponse> Invites);

public sealed record InviteOpenResponse(
    string Email,
    bool UserExists,
    bool Consumed);

