namespace Workslip.Application.Users;

public interface IUserService
{
    Task<(bool Success, UserResponse? User, IReadOnlyList<string>? Errors)> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken);

    Task<(bool Success, UserResponse? User, IReadOnlyList<string>? Errors)> GetAsync(Guid userId, CancellationToken cancellationToken);

    Task<(bool Success, UserListResponse? Users, IReadOnlyList<string>? Errors)> GetByOrganizationAsync(Guid organizationId, CancellationToken cancellationToken);

    Task<(bool Success, UserResponse? User, IReadOnlyList<string>? Errors)> UpdateAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken);

    Task<(bool Success, IReadOnlyList<string>? Errors)> DeleteAsync(Guid userId, CancellationToken cancellationToken);
}