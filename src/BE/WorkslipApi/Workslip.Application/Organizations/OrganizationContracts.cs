namespace Workslip.Application.Organizations;

public sealed record CreateOrganizationRequest(
    string Name,
    string Cvr,
    string AdminDisplayName,
    string? AdminEmail,
    string? AdminPhone);

public sealed record UpsertOrganizationAdminRequest(
    string Email,
    string DisplayName,
    string? Phone);

public sealed record OrganizationResponse(
    Guid Id,
    string Name,
    string Cvr,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record OrganizationUserResponse(
    Guid Id,
    Guid OrganizationId,
    string DisplayName,
    string? Email,
    string? Phone,
    string Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record OrganizationOnboardingResponse(
    OrganizationResponse Organization,
    OrganizationUserResponse User);

public sealed record CurrentUserResponse(
    Guid Id,
    string DisplayName,
    string? Email,
    string? Phone,
    string Role,
    OrganizationResponse Organization);
