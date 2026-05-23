using FluentValidation;
using Microsoft.Extensions.Logging;
using Workslip.Application.Users;

namespace Workslip.Application.Users;

public sealed class UserService(
    IUserRepository repository,
    IValidator<CreateUserRequest> createUserValidator,
    IValidator<UpdateUserRequest> updateUserValidator,
    ILogger<UserService> logger) : IUserService
{
    public async Task<(bool Success, UserResponse? User, IReadOnlyList<string>? Errors)> CreateAsync(
        CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await createUserValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors.Select(e => e.ErrorMessage).ToList();
            logger.LogWarning("User create validation failed. Errors: {Errors}", string.Join(", ", errors));
            return (false, null, errors);
        }

        var existing = await repository.GetByEmailAsync(request.Email, cancellationToken);
        if (existing != null)
            return (false, null, ["Email already in use"]);

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

        logger.LogInformation("User created. UserId: {UserId}. OrganizationId: {OrganizationId}. Role: {Role}.", user.Id, user.OrganizationId, user.Role);

        return (true, MapToResponse(user), null);
    }

    public async Task<(bool Success, UserResponse? User, IReadOnlyList<string>? Errors)> GetAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await repository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
            return (false, null, ["User not found"]);

        return (true, MapToResponse(user), null);
    }

    public async Task<(bool Success, UserListResponse? Users, IReadOnlyList<string>? Errors)> GetByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var users = await repository.GetByOrganizationIdAsync(organizationId, cancellationToken);
        var count = await repository.GetCountByOrganizationIdAsync(organizationId, cancellationToken);

        var responses = users.Select(MapToResponse).ToList();
        return (true, new UserListResponse(responses, count), null);
    }

    public async Task<(bool Success, UserResponse? User, IReadOnlyList<string>? Errors)> UpdateAsync(
        Guid userId,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await updateUserValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors.Select(e => e.ErrorMessage).ToList();
            logger.LogWarning("User update validation failed. Errors: {Errors}", string.Join(", ", errors));
            return (false, null, errors);
        }

        var user = await repository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
            return (false, null, ["User not found"]);

        if (!string.IsNullOrEmpty(request.DisplayName))
            user.DisplayName = request.DisplayName;

        if (!string.IsNullOrEmpty(request.Phone))
            user.Phone = request.Phone;

        if (!string.IsNullOrEmpty(request.Role))
            user.Role = request.Role;

        user.UpdatedAt = DateTimeOffset.UtcNow;

        await repository.UpdateAsync(user, cancellationToken);

        logger.LogInformation("User updated. UserId: {UserId}.", userId);

        return (true, MapToResponse(user), null);
    }

    public async Task<(bool Success, IReadOnlyList<string>? Errors)> DeleteAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await repository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
            return (false, ["User not found"]);

        await repository.DeleteAsync(userId, cancellationToken);

        logger.LogInformation("User deleted. UserId: {UserId}.", userId);

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
