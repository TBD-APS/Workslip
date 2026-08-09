using Ardalis.Result;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;
using Workslip.Application.Organizations;
using Workslip.Domain.Models;

namespace Workslip.Application.Users;

public sealed class SuperadminUserService(
    IOrganizationAdministrationRepository repository,
    IValidator<CreateAdminUserRequest> createUserValidator,
    IValidator<UpdateAdminUserRequest> updateUserValidator,
    IUserEntraService entraService,
    ICurrentUserContext currentUser,
    ILogger<SuperadminUserService> logger) : ISuperadminUserService
{
    public async Task<Result<AdminUserListResponse>> ListAsync(
        Guid? organizationId,
        int? limit,
        int? offset,
        string? search,
        string? sortBy,
        string? sortDirection,
        CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(limit ?? 50, 1, 200);
        var normalizedOffset = Math.Max(offset ?? 0, 0);

        var rows = await repository.ListUsersAsync(
            organizationId, normalizedLimit, normalizedOffset, search, sortBy, sortDirection, cancellationToken);
        var total = await repository.CountUsersAsync(organizationId, search, cancellationToken);

        return Result<AdminUserListResponse>.Success(
            new AdminUserListResponse(rows.Select(ToResponse).ToList(), total));
    }

    public async Task<Result<AdminUserResponse>> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await repository.GetUserWithOrganizationAsync(userId, cancellationToken);
        if (row is null)
        {
            logger.LogInformation("Admin user not found. UserId: {UserId}.", userId);
            return Result<AdminUserResponse>.NotFound();
        }

        return Result<AdminUserResponse>.Success(ToResponse(row));
    }

    public async Task<Result<AdminUserResponse>> CreateAsync(CreateAdminUserRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await createUserValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = MapValidationErrors(validationResult);
            logger.LogWarning("Admin user create validation failed. Fields: {Fields}",
                string.Join(", ", errors.Select(e => e.Identifier).Distinct()));

            return Result<AdminUserResponse>.Invalid(errors);
        }

        var organization = await repository.GetOrganizationAsync(request.OrganizationId, cancellationToken);
        if (organization is null)
        {
            logger.LogInformation("Admin user create denied: organization not found. OrganizationId: {OrganizationId}.", request.OrganizationId);
            return Result<AdminUserResponse>.NotFound();
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var existing = await repository.GetUserByEmailAsync(normalizedEmail, cancellationToken);
        if (existing != null)
        {
            logger.LogWarning("Admin user create conflict: email already in use. Email: {Email}", normalizedEmail);
            return Result<AdminUserResponse>.Conflict("email_in_use");
        }

        var entraUser = await entraService.CreateUserAsync(normalizedEmail, request.DisplayName.Trim(), cancellationToken);
        var user = BuildUserRow(normalizedEmail, request, entraUser);

        var userId = await repository.CreateUserAsync(user, cancellationToken);
        if (userId is null)
        {
            await TryRollbackCreatedEntraUserAsync(entraUser, cancellationToken);
            logger.LogWarning("Admin user create conflict after insert attempt. Email: {Email}.", normalizedEmail);
            return Result<AdminUserResponse>.Conflict("email_in_use");
        }

        user.Id = userId.Value;

        logger.LogInformation(
            "Admin user created. UserId: {UserId}. OrganizationId: {OrganizationId}. Role: {Role}.",
            user.Id, user.OrganizationId, user.Role);

        return Result<AdminUserResponse>.Success(new AdminUserResponse(
            user.Id,
            user.OrganizationId,
            organization.Name,
            user.Email,
            user.DisplayName,
            user.Phone,
            user.Role,
            user.CreatedAt,
            user.UpdatedAt));
    }

    public async Task<Result<AdminUserResponse>> UpdateAsync(
        Guid userId,
        UpdateAdminUserRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await updateUserValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = MapValidationErrors(validationResult);
            logger.LogWarning("Admin user update validation failed. Fields: {Fields}",
                string.Join(", ", errors.Select(e => e.Identifier).Distinct()));

            return Result<AdminUserResponse>.Invalid(errors);
        }

        if (!string.IsNullOrEmpty(request.Role) && IsActingOnSelf(userId))
        {
            logger.LogWarning("Admin user update denied: Superadmin cannot change their own role. UserId: {UserId}.", userId);
            return Result<AdminUserResponse>.Conflict("self_action_not_allowed");
        }

        var row = await repository.GetUserWithOrganizationAsync(userId, cancellationToken);
        if (row is null)
        {
            logger.LogInformation("Admin user not found for update. UserId: {UserId}.", userId);
            return Result<AdminUserResponse>.NotFound();
        }

        var user = row.User;
        var expectedEmail = user.Email;
        var expectedEntraId = user.EntraId;

        if (!string.IsNullOrEmpty(request.DisplayName))
            user.DisplayName = request.DisplayName;

        if (!string.IsNullOrEmpty(request.Phone))
            user.Phone = request.Phone;

        if (!string.IsNullOrEmpty(request.Role))
            user.Role = request.Role;

        user.UpdatedAt = DateTimeOffset.UtcNow;

        var updated = await repository.UpdateUserAsync(user, expectedEmail, expectedEntraId, cancellationToken);
        if (!updated)
        {
            logger.LogWarning("Admin user update conflict: user changed concurrently. UserId: {UserId}.", userId);
            return Result<AdminUserResponse>.Conflict("user_state_changed");
        }

        logger.LogInformation("Admin user updated. UserId: {UserId}.", userId);

        return Result<AdminUserResponse>.Success(ToResponse(row));
    }

    public async Task<Result> DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (IsActingOnSelf(userId))
        {
            logger.LogWarning("Admin user delete denied: Superadmin cannot delete their own account. UserId: {UserId}.", userId);
            return Result.Conflict("self_action_not_allowed");
        }

        var row = await repository.GetUserWithOrganizationAsync(userId, cancellationToken);
        if (row is null)
        {
            logger.LogInformation("Admin user not found for deletion. UserId: {UserId}.", userId);
            return Result.NotFound();
        }

        var deleted = await repository.DeleteUserAsync(userId, cancellationToken);
        if (!deleted)
        {
            logger.LogInformation("Admin user not found for deletion. UserId: {UserId}.", userId);
            return Result.NotFound();
        }

        logger.LogInformation("Admin user deleted. UserId: {UserId}.", userId);

        return Result.NoContent();
    }

    private bool IsActingOnSelf(Guid userId) => currentUser.UserId == userId;

    private async Task TryRollbackCreatedEntraUserAsync(CreateEntraUserResult entraUser, CancellationToken cancellationToken)
    {
        if (!entraUser.Created)
        {
            return;
        }

        try
        {
            if (await repository.IsEntraIdentityReferencedAsync(entraUser.EntraUserId, cancellationToken))
            {
                logger.LogWarning(
                    "Skipping Entra rollback because identity is already referenced. EntraUserId: {EntraUserId}.",
                    entraUser.EntraUserId);
                return;
            }

            await entraService.DeleteUserAsync(entraUser.EntraUserId, cancellationToken);
        }
        catch (Exception rollbackException)
        {
            logger.LogError(
                rollbackException,
                "Admin user create Entra rollback failed. EntraUserId: {EntraUserId}.",
                entraUser.EntraUserId);
        }
    }

    private static UserDataRow BuildUserRow(string normalizedEmail, CreateAdminUserRequest request, CreateEntraUserResult entraUser) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrganizationId,
            Email = normalizedEmail,
            DisplayName = request.DisplayName.Trim(),
            EntraEmail = entraUser.EntraMail,
            EntraId = entraUser.EntraUserId,
            Phone = request.Phone?.Trim() ?? string.Empty,
            Role = request.Role,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static AdminUserResponse ToResponse(OrganizationUserRow row) => new(
        row.User.Id,
        row.User.OrganizationId,
        row.OrganizationName,
        row.User.Email,
        row.User.DisplayName,
        row.User.Phone,
        row.User.Role,
        row.User.CreatedAt,
        row.User.UpdatedAt);

    private static List<ValidationError> MapValidationErrors(ValidationResult result) =>
        result.Errors
            .Select(e => new ValidationError { Identifier = e.PropertyName, ErrorMessage = e.ErrorMessage })
            .ToList();
}
