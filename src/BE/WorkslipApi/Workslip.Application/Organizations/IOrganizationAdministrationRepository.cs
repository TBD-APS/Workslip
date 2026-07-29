using Workslip.Domain.Models;

namespace Workslip.Application.Organizations;

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
}
