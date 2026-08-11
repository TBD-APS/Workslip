namespace Workslip.Application.Users;

public sealed record SuperAdminCreateUserRequest(
    Guid OrganizationId,
    Guid FilialId,
    string Email,
    string DisplayName,
    string Phone,
    string Role);

public sealed record SuperAdminUpdateUserRequest(
    string? DisplayName,
    string? Phone,
    string? Role,
    Guid? FilialId);

public sealed record SuperAdminUserResponse(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    Guid FilialId,
    string FilialName,
    string Email,
    string DisplayName,
    string Phone,
    string Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SuperAdminUserListResponse(
    IReadOnlyList<SuperAdminUserResponse> Users,
    int Total);

public sealed record SuperAdminFilialOptionResponse(
    Guid Id,
    string Name,
    bool IsDefault);

public sealed record SuperAdminOrganizationOptionResponse(
    Guid Id,
    string Name,
    IReadOnlyList<SuperAdminFilialOptionResponse> Filials);

public sealed record SuperAdminUserOptionsResponse(
    IReadOnlyList<SuperAdminOrganizationOptionResponse> Organizations,
    IReadOnlyList<string> Roles);
