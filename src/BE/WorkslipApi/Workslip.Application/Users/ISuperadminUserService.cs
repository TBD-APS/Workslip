using Ardalis.Result;

namespace Workslip.Application.Users;

public interface ISuperadminUserService
{
    Task<Result<AdminUserListResponse>> ListAsync(
        Guid? organizationId,
        int? limit,
        int? offset,
        string? search,
        string? sortBy,
        string? sortDirection,
        CancellationToken cancellationToken);

    Task<Result<AdminUserResponse>> GetAsync(Guid userId, CancellationToken cancellationToken);

    Task<Result<AdminUserResponse>> CreateAsync(CreateAdminUserRequest request, CancellationToken cancellationToken);

    Task<Result<AdminUserResponse>> UpdateAsync(Guid userId, UpdateAdminUserRequest request, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(Guid userId, CancellationToken cancellationToken);
}
