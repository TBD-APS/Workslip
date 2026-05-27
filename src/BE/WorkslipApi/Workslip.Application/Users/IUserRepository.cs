using Workslip.Domain.Models;

namespace Workslip.Application.Users;

public interface IUserRepository
{
    Task<UserDataRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<UserDataRow?> GetByEmailAsync(string email, CancellationToken cancellationToken);
    Task<UserDataRow?> GetByExternalIdentityAsync(string? entraId, string? email, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserDataRow>> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<int> GetCountByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<Guid> CreateAsync(UserDataRow user, CancellationToken cancellationToken);
    Task UpdateAsync(UserDataRow user, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
