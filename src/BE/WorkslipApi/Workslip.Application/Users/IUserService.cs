using Ardalis.Result;

namespace Workslip.Application.Users;

public interface IUserService
{
    Task<Result<UserResponse>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken);

    Task<Result<UserResponse>> GetAsync(Guid userId, CancellationToken cancellationToken);

    Task<Result<UserDetailResponse>> GetDetailAsync(Guid userId, CancellationToken cancellationToken);

    Task<Result<UserListResponse>> GetByOrganizationAsync(int? limit, int? offset, string? search, string? sortBy, string? sortDirection, CancellationToken cancellationToken);

    Task<Result<UserResponse>> UpdateAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken);

    Task<Result<UserResponse>> SetUserKindAsync(Guid userId, SetUserKindRequest request, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(Guid userId, CancellationToken cancellationToken);
}
