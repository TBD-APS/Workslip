using Workslip.Domain.Models;

namespace Workslip.Application.Organizations;

public sealed record OrganizationUserRow(UserDataRow User, string OrganizationName);

public interface IOrganizationAdministrationRepository
{
    Task<IReadOnlyList<OrganizationRow>> ListOrganizationsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OrganizationRow>>([]);

    Task<OrganizationRow?> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<UserDataRow?> GetUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);

    Task<UserDataRow?> GetUnlinkedAdminAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<bool> IsEntraIdentityReferencedAsync(string entraUserId, CancellationToken cancellationToken);

    Task<Guid?> CreateAdminAsync(UserDataRow admin, CancellationToken cancellationToken);

    Task<bool> UpdateAdminAsync(
        UserDataRow admin,
        string expectedEmail,
        string expectedEntraId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizationUserRow>> ListUsersAsync(
        Guid? organizationId,
        int limit,
        int offset,
        string? search,
        string? sortBy,
        string? sortDirection,
        CancellationToken cancellationToken);

    Task<int> CountUsersAsync(Guid? organizationId, string? search, CancellationToken cancellationToken);

    Task<OrganizationUserRow?> GetUserWithOrganizationAsync(Guid userId, CancellationToken cancellationToken);

    Task<Guid?> CreateUserAsync(UserDataRow user, CancellationToken cancellationToken);

    Task<bool> UpdateUserAsync(
        UserDataRow user,
        string expectedEmail,
        string expectedEntraId,
        CancellationToken cancellationToken);

    Task<bool> DeleteUserAsync(Guid userId, CancellationToken cancellationToken);
}
