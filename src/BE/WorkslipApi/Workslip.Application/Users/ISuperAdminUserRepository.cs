using Workslip.Domain.Models;

namespace Workslip.Application.Users;

public sealed record SuperAdminUserRecord(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    Guid FilialId,
    string FilialName,
    string Email,
    string EntraEmail,
    string EntraId,
    string DisplayName,
    string Phone,
    string Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SuperAdminFilialRecord(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    string Name,
    bool IsDefault);

public enum SuperAdminUserDeleteStatus
{
    Deleted,
    NotFound,
    HasHistory
}

public interface ISuperAdminUserRepository
{
    Task<IReadOnlyList<SuperAdminUserRecord>> ListAsync(
        int limit,
        int offset,
        string? search,
        string? sortBy,
        string? sortDirection,
        CancellationToken cancellationToken);

    Task<int> CountAsync(string? search, CancellationToken cancellationToken);

    Task<SuperAdminUserRecord?> GetAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SuperAdminFilialRecord>> ListFilialsAsync(CancellationToken cancellationToken);

    Task<bool> TenantFilialExistsAsync(
        Guid organizationId,
        Guid filialId,
        CancellationToken cancellationToken);

    Task<UserDataRow?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    Task<Guid?> CreateAsync(UserDataRow user, CancellationToken cancellationToken);

    Task<bool> UpdateAsync(
        Guid userId,
        string displayName,
        string phone,
        string role,
        Guid filialId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken);

    Task<SuperAdminUserDeleteStatus> DeleteAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> IsEntraIdentityReferencedAsync(string entraUserId, CancellationToken cancellationToken);
}
