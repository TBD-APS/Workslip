namespace Workslip.Application.Users;

public interface IUserService
{
    Task<(bool Success, UserResponse? User, IReadOnlyList<string>? Errors)> CreateAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken);

    Task<(bool Success, UserResponse? User, IReadOnlyList<string>? Errors)> GetAsync(
        Guid userId,
        CancellationToken cancellationToken);
}