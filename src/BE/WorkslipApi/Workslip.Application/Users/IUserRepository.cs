using Workslip.Domain.Models;

namespace Workslip.Application.Users;

public interface IUserRepository
{
    Task<UserDataRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<UserDataRow?> GetByEmailAsync(string email, CancellationToken cancellationToken);
    Task<UserDataRow?> GetByExternalIdentityAsync(string? entraId, IReadOnlyCollection<string> emailCandidates, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserDataRow>> GetByOrganizationIdAsync(Guid organizationId, int limit, int offset, string? search, string? sortBy, string? sortDirection, CancellationToken cancellationToken);
    Task<int> GetCountByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<Guid> CreateAsync(UserDataRow user, CancellationToken cancellationToken);
    Task UpdateAsync(UserDataRow user, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<AssignedJobResponse>> GetAssignedJobsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);
    Task<decimal?> GetTotalHoursAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken);
}
