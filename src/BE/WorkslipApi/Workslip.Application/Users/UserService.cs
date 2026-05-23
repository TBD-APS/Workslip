using Workslip.Application.Users;
namespace Workslip.Application.Users;

public sealed class UserService(IUserRepository repository)
{
    private static readonly string[] ValidRoles = ["Superadmin", "Admin", "User"];

    public async Task<(bool Success, UserResponse? User, string? Error)> CreateAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        // Validate role
        if (!ValidRoles.Contains(request.Role))
            return (false, null, "Invalid role");

        // Check email doesn't exist
        var existing = await repository.GetByEmailAsync(request.Email, cancellationToken);
        if (existing != null)
            return (false, null, "Email already in use");

        var user = new UserData
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrganizationId,
            Email = request.Email,
            DisplayName = request.DisplayName,
            Phone = request.Phone,
            Role = request.Role,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var userId = await repository.CreateAsync(user, cancellationToken);
        user.Id = userId;

        return (true, MapToResponse(user), null);
    }

    public async Task<(bool Success, UserResponse? User, string? Error)> GetAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await repository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
            return (false, null, "User not found");

        return (true, MapToResponse(user), null);
    }

    public async Task<(bool Success, UserListResponse? Users, string? Error)> GetByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var users = await repository.GetByOrganizationIdAsync(organizationId, cancellationToken);
        var count = await repository.GetCountByOrganizationIdAsync(organizationId, cancellationToken);

        var responses = users.Select(MapToResponse).ToList();
        return (true, new UserListResponse(responses, count), null);
    }

    public async Task<(bool Success, UserResponse? User, string? Error)> UpdateAsync(
        Guid userId,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await repository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
            return (false, null, "User not found");

        // Validate role if provided
        if (!string.IsNullOrEmpty(request.Role) && !ValidRoles.Contains(request.Role))
            return (false, null, "Invalid role");

        if (!string.IsNullOrEmpty(request.DisplayName))
            user.DisplayName = request.DisplayName;

        if (!string.IsNullOrEmpty(request.Phone))
            user.Phone = request.Phone;

        if (!string.IsNullOrEmpty(request.Role))
            user.Role = request.Role;

        user.UpdatedAt = DateTimeOffset.UtcNow;

        await repository.UpdateAsync(user, cancellationToken);

        return (true, MapToResponse(user), null);
    }

    public async Task<(bool Success, string? Error)> DeleteAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await repository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
            return (false, "User not found");

        await repository.DeleteAsync(userId, cancellationToken);

        return (true, null);
    }

    private static UserResponse MapToResponse(UserData user) =>
        new(
            user.Id,
            user.OrganizationId,
            user.Email,
            user.DisplayName,
            user.Phone,
            user.Role,
            user.CreatedAt,
            user.UpdatedAt);
}
