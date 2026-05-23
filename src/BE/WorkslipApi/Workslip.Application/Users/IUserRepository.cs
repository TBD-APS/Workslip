namespace Workslip.Application.Users;

public interface IUserRepository
{
    Task<UserData?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<UserData?> GetByEmailAsync(string email, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserData>> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<int> GetCountByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<Guid> CreateAsync(UserData user, CancellationToken cancellationToken);
    Task UpdateAsync(UserData user, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}
