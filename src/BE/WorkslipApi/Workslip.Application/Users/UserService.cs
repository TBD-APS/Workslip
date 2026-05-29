using Ardalis.Result;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;
using Workslip.Domain.Models;

namespace Workslip.Application.Users;

public sealed class UserService(
    IUserRepository repository,
    IValidator<CreateUserRequest> createUserValidator,
    IValidator<UpdateUserRequest> updateUserValidator,
    IUserEntraService entraService,
    ICurrentUserContext currentUser,
    ILogger<UserService> logger) : IUserService
{
    public async Task<Result<UserResponse>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await createUserValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .Select(e => new ValidationError
                {
                    Identifier = e.PropertyName,
                    ErrorMessage = e.ErrorMessage
                })
                .ToList();

            logger.LogWarning("User create validation failed. Fields: {Fields}",
                string.Join(", ", errors.Select(e => e.Identifier).Distinct()));

            return Result<UserResponse>.Invalid(errors);
        }

        var existing = await repository.GetByEmailAsync(request.Email, cancellationToken);
        if (existing != null)
        {
            logger.LogWarning("User create conflict: email already in use. Email: {Email}", request.Email);
            return Result<UserResponse>.Conflict("email_in_use");
        }

        var entraUser = await entraService.CreateUserAsync(request.Email, request.DisplayName, cancellationToken);

        var user = new UserDataRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = currentUser.OrganizationId.GetValueOrDefault(),
            Email = request.Email,
            DisplayName = request.DisplayName,
            EntraEmail = entraUser.EntraMail,
            EntraId = entraUser.EntraUserId,
            Phone = request.Phone,
            Role = request.Role,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var userId = await repository.CreateAsync(user, cancellationToken);
        user.Id = userId;

        logger.LogInformation("User created. UserId: {UserId}. OrganizationId: {OrganizationId}. Role: {Role}.", user.Id, user.OrganizationId, user.Role);

        return Result<UserResponse>.Success(MapToResponse(user));
    }

    public async Task<Result<UserResponse>> GetAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<UserResponse>.Unauthorized();
        }

        var user = await repository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            logger.LogInformation("User not found. UserId: {UserId}.", userId);
            return Result<UserResponse>.NotFound();
        }

        return Result<UserResponse>.Success(MapToResponse(user));
    }

    public async Task<Result<UserListResponse>> GetByOrganizationAsync(
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<UserListResponse>.Unauthorized();
        }

        var users = await repository.GetByOrganizationIdAsync(organizationId.Value, cancellationToken);
        var count = await repository.GetCountByOrganizationIdAsync(organizationId.Value, cancellationToken);

        var responses = users.Select(MapToResponse).ToList();
        return Result<UserListResponse>.Success(new UserListResponse(responses, count));
    }

    public async Task<Result<UserResponse>> UpdateAsync(
        Guid userId,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await updateUserValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .Select(e => new ValidationError
                {
                    Identifier = e.PropertyName,
                    ErrorMessage = e.ErrorMessage
                })
                .ToList();

            logger.LogWarning("User update validation failed. Fields: {Fields}",
                string.Join(", ", errors.Select(e => e.Identifier).Distinct()));

            return Result<UserResponse>.Invalid(errors);
        }

        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<UserResponse>.Unauthorized();
        }

        var user = await repository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            logger.LogInformation("User not found for update. UserId: {UserId}.", userId);
            return Result<UserResponse>.NotFound();
        }

        if (!string.IsNullOrEmpty(request.DisplayName))
            user.DisplayName = request.DisplayName;

        if (!string.IsNullOrEmpty(request.Phone))
            user.Phone = request.Phone;

        if (!string.IsNullOrEmpty(request.Role))
            user.Role = request.Role;

        user.UpdatedAt = DateTimeOffset.UtcNow;

        await repository.UpdateAsync(user, cancellationToken);

        logger.LogInformation("User updated. UserId: {UserId}.", userId);

        return Result<UserResponse>.Success(MapToResponse(user));
    }

    public async Task<Result> DeleteAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result.Unauthorized();
        }

        var user = await repository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            logger.LogInformation("User not found for deletion. UserId: {UserId}.", userId);
            return Result.NotFound();
        }

        await repository.DeleteAsync(userId, cancellationToken);

        logger.LogInformation("User deleted. UserId: {UserId}.", userId);

        return Result.NoContent();
    }

    private static UserResponse MapToResponse(UserDataRow user) =>
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
