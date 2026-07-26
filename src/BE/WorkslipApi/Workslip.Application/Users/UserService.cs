using Ardalis.Result;
using FluentValidation;
using FluentValidation.Results;
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
            var errors = MapValidationErrors(validationResult);
            logger.LogWarning("User create validation failed. Fields: {Fields}",
                string.Join(", ", errors.Select(e => e.Identifier).Distinct()));

            return Result<UserResponse>.Invalid(errors);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var existing = await repository.GetByEmailAsync(normalizedEmail, cancellationToken);
        if (existing != null)
        {
            logger.LogWarning("User create conflict: email already in use. Email: {Email}", normalizedEmail);
            return Result<UserResponse>.Conflict("email_in_use");
        }

        var entraUser = await entraService.CreateUserAsync(normalizedEmail, request.DisplayName, cancellationToken);

        var user = BuildUserRow(normalizedEmail, request, entraUser, currentUser.OrganizationId.GetValueOrDefault());
        var userId = await repository.CreateAsync(user, cancellationToken);
        user.Id = userId;

        logger.LogInformation("User created. UserId: {UserId}. OrganizationId: {OrganizationId}. Role: {Role}.", user.Id, user.OrganizationId, user.Role);

        return Result<UserResponse>.Success(UserResponseBuilder.MapToResponse(user));
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

        return Result<UserResponse>.Success(UserResponseBuilder.MapToResponse(user));
    }

    public async Task<Result<UserDetailResponse>> GetDetailAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<UserDetailResponse>.Unauthorized();
        }

        var user = await repository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            logger.LogInformation("User not found. UserId: {UserId}.", userId);
            return Result<UserDetailResponse>.NotFound();
        }

        var assignedJobs = await repository.GetAssignedJobsAsync(organizationId.Value, userId, cancellationToken);
        var totalHours = await repository.GetTotalHoursAsync(organizationId.Value, userId, cancellationToken);
        var periodHours = await repository.GetPeriodHoursAsync(organizationId.Value, ComputeBiweeklyStart(), cancellationToken);
        var hours = periodHours.GetValueOrDefault(userId);

        return Result<UserDetailResponse>.Success(new UserDetailResponse(
            user.Id,
            user.OrganizationId,
            user.Email,
            user.DisplayName,
            user.Phone,
            user.Role,
            assignedJobs,
            totalHours,
            hours?.HoursThisWeek,
            hours?.HoursThisMonth,
            hours?.HoursBiweekly));
    }

    public async Task<Result<UserListResponse>> GetByOrganizationAsync(
        int? limit,
        int? offset,
        string? search,
        string? sortBy,
        string? sortDirection,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<UserListResponse>.Unauthorized();
        }

        var normalizedLimit = Math.Clamp(limit ?? 50, 1, 200);
        var normalizedOffset = Math.Max(offset ?? 0, 0);
        var users = await repository.GetByOrganizationIdAsync(organizationId.Value, normalizedLimit, normalizedOffset, search, sortBy, sortDirection, cancellationToken);
        var count = await repository.GetCountByOrganizationIdAsync(organizationId.Value, cancellationToken);
        var periodHours = await repository.GetPeriodHoursAsync(organizationId.Value, ComputeBiweeklyStart(), cancellationToken);

        var responses = users.Select(u =>
        {
            var response = UserResponseBuilder.MapToResponse(u);
            var hours = periodHours.GetValueOrDefault(u.Id);
            if (hours is not null)
            {
                response = response with
                {
                    HoursThisWeek = hours.HoursThisWeek,
                    HoursThisMonth = hours.HoursThisMonth,
                    HoursBiweekly = hours.HoursBiweekly
                };
            }
            return response;
        }).ToList();

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
            var errors = MapValidationErrors(validationResult);
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

        return Result<UserResponse>.Success(UserResponseBuilder.MapToResponse(user));
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

    private static List<ValidationError> MapValidationErrors(ValidationResult result) =>
        result.Errors
            .Select(e => new ValidationError { Identifier = e.PropertyName, ErrorMessage = e.ErrorMessage })
            .ToList();

    private static UserDataRow BuildUserRow(string normalizedEmail, CreateUserRequest request, CreateEntraUserResult entraUser, Guid organizationId) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Email = normalizedEmail,
            DisplayName = request.DisplayName,
            EntraEmail = entraUser.EntraMail,
            EntraId = entraUser.EntraUserId,
            Phone = request.Phone,
            Role = request.Role,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static DateOnly ComputeBiweeklyStart()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var thisMonday = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
        return thisMonday.AddDays(-14);
    }
}
