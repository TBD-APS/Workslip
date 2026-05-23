namespace Workslip.Application.Users;

public sealed record CreateUserRequest(
    Guid OrganizationId,
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
    string Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UserListResponse(
    IReadOnlyList<UserResponse> Users,
    int Total);
