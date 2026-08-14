using Ardalis.Result;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Application.Users;

public interface ISuperAdminUserService
{
    Task<Result<SuperAdminUserListResponse>> ListAsync(
        int? limit,
        int? offset,
        string? search,
        string? sortBy,
        string? sortDirection,
        CancellationToken cancellationToken);

    Task<Result<SuperAdminUserOptionsResponse>> GetOptionsAsync(CancellationToken cancellationToken);

    Task<Result<SuperAdminUserResponse>> CreateAsync(
        SuperAdminCreateUserRequest request,
        CancellationToken cancellationToken);

    Task<Result<SuperAdminUserResponse>> UpdateAsync(
        Guid userId,
        SuperAdminUpdateUserRequest request,
        CancellationToken cancellationToken);

    Task<Result> DeleteAsync(Guid userId, CancellationToken cancellationToken);
}

public sealed class SuperAdminUserService(
    ISuperAdminUserRepository repository,
    IValidator<CreateUserRequest> createUserValidator,
    IValidator<UpdateUserRequest> updateUserValidator,
    IUserEntraService entraService,
    IUserClaimsCacheInvalidator claimsCache,
    ICurrentUserContext currentUser,
    ILogger<SuperAdminUserService> logger) : ISuperAdminUserService
{
    private static readonly IReadOnlyList<string> TenantRoles =
        [Roles.User, Roles.Admin, Roles.Auditor];

    private static readonly IReadOnlyList<string> TenantUserKinds =
        [UserKinds.Member, UserKinds.InternalTest];

    public async Task<Result<SuperAdminUserListResponse>> ListAsync(
        int? limit,
        int? offset,
        string? search,
        string? sortBy,
        string? sortDirection,
        CancellationToken cancellationToken)
    {
        if (!IsSuperadminActor())
        {
            return Result<SuperAdminUserListResponse>.Forbidden();
        }

        var normalizedLimit = Math.Clamp(limit ?? 50, 1, 200);
        var normalizedOffset = Math.Max(offset ?? 0, 0);
        var users = await repository.ListAsync(
            normalizedLimit,
            normalizedOffset,
            search,
            sortBy,
            sortDirection,
            cancellationToken);
        var count = await repository.CountAsync(search, cancellationToken);

        return Result<SuperAdminUserListResponse>.Success(new(
            users.Select(ToResponse).ToArray(),
            count));
    }

    public async Task<Result<SuperAdminUserOptionsResponse>> GetOptionsAsync(
        CancellationToken cancellationToken)
    {
        if (!IsSuperadminActor())
        {
            return Result<SuperAdminUserOptionsResponse>.Forbidden();
        }

        var filials = await repository.ListFilialsAsync(cancellationToken);
        var organizations = filials
            .GroupBy(filial => new { filial.OrganizationId, filial.OrganizationName })
            .OrderBy(group => group.Key.OrganizationName)
            .Select(group => new SuperAdminOrganizationOptionResponse(
                group.Key.OrganizationId,
                group.Key.OrganizationName,
                group
                    .OrderByDescending(filial => filial.IsDefault)
                    .ThenBy(filial => filial.Name)
                    .Select(filial => new SuperAdminFilialOptionResponse(
                        filial.Id,
                        filial.Name,
                        filial.IsDefault))
                    .ToArray()))
            .ToArray();

        return Result<SuperAdminUserOptionsResponse>.Success(new(
            organizations,
            TenantRoles,
            TenantUserKinds));
    }

    public async Task<Result<SuperAdminUserResponse>> CreateAsync(
        SuperAdminCreateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsSuperadminActor())
        {
            return Result<SuperAdminUserResponse>.Forbidden();
        }

        var commonRequest = new CreateUserRequest(
            request.Email,
            request.DisplayName,
            request.Phone,
            request.Role);
        var validationResult = await createUserValidator.ValidateAsync(commonRequest, cancellationToken);
        var errors = MapValidationErrors(validationResult);
        AddTenantRoleError(errors, request.Role);
        var userKind = NormalizeRequestedUserKind(request.UserKind);
        AddUserKindError(errors, request.UserKind, userKind);
        if (request.OrganizationId == Guid.Empty)
        {
            errors.Add(new ValidationError
            {
                Identifier = nameof(request.OrganizationId),
                ErrorMessage = "Organisation er påkrævet."
            });
        }
        if (request.FilialId == Guid.Empty)
        {
            errors.Add(new ValidationError
            {
                Identifier = nameof(request.FilialId),
                ErrorMessage = "Filial er påkrævet."
            });
        }
        if (errors.Count > 0)
        {
            return Result<SuperAdminUserResponse>.Invalid(errors);
        }

        if (!await repository.TenantFilialExistsAsync(
                request.OrganizationId,
                request.FilialId,
                cancellationToken))
        {
            return Result<SuperAdminUserResponse>.Invalid([
                new ValidationError
                {
                    Identifier = nameof(request.FilialId),
                    ErrorMessage = "Den valgte filial tilhører ikke den valgte organisation."
                }
            ]);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (await repository.GetByEmailAsync(normalizedEmail, cancellationToken) is not null)
        {
            return Result<SuperAdminUserResponse>.Conflict("email_in_use");
        }

        var entraUser = await entraService.CreateUserAsync(
            normalizedEmail,
            request.DisplayName.Trim(),
            cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var user = new UserDataRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrganizationId,
            FilialId = request.FilialId,
            Email = normalizedEmail,
            DisplayName = request.DisplayName.Trim(),
            Phone = request.Phone.Trim(),
            EntraId = entraUser.EntraUserId,
            EntraEmail = entraUser.EntraMail,
            Role = request.Role,
            UserKind = userKind!,
            CreatedAt = now,
            UpdatedAt = now
        };

        try
        {
            var createdId = await repository.CreateAsync(user, cancellationToken);
            if (createdId is null)
            {
                await TryCompensateEntraCreateAsync(entraUser, cancellationToken);
                return Result<SuperAdminUserResponse>.Conflict("email_in_use");
            }

            var created = await repository.GetAsync(createdId.Value, cancellationToken);
            if (created is null)
            {
                logger.LogError(
                    "Superadmin user create committed but read-back failed. UserId: {UserId}. OrganizationId: {OrganizationId}.",
                    createdId,
                    request.OrganizationId);
                return Result<SuperAdminUserResponse>.Error("superadmin_user_readback_failed");
            }

            logger.LogInformation(
                "Superadmin created tenant user. UserId: {UserId}. OrganizationId: {OrganizationId}. FilialId: {FilialId}. Role: {Role}. UserKind: {UserKind}.",
                created.Id,
                created.OrganizationId,
                created.FilialId,
                created.Role.Replace("\r", " ").Replace("\n", " "),
                created.UserKind);

            return Result<SuperAdminUserResponse>.Success(ToResponse(created));
        }
        catch
        {
            await TryCompensateEntraCreateAsync(entraUser, CancellationToken.None);
            throw;
        }
    }

    public async Task<Result<SuperAdminUserResponse>> UpdateAsync(
        Guid userId,
        SuperAdminUpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsSuperadminActor())
        {
            return Result<SuperAdminUserResponse>.Forbidden();
        }

        var current = await repository.GetAsync(userId, cancellationToken);
        if (current is null)
        {
            return Result<SuperAdminUserResponse>.NotFound();
        }

        var commonRequest = new UpdateUserRequest(
            request.DisplayName,
            request.Phone,
            request.Role);
        var validationResult = await updateUserValidator.ValidateAsync(commonRequest, cancellationToken);
        var errors = MapValidationErrors(validationResult);
        if (request.DisplayName is not null && string.IsNullOrWhiteSpace(request.DisplayName))
        {
            errors.Add(new ValidationError
            {
                Identifier = nameof(request.DisplayName),
                ErrorMessage = "Visningsnavn må ikke være tomt."
            });
        }
        if (request.Role is not null)
        {
            AddTenantRoleError(errors, request.Role);
        }

        var requestedUserKind = request.UserKind is null
            ? UserKinds.Normalize(current.UserKind) ?? UserKinds.Member
            : UserKinds.Normalize(request.UserKind);
        AddUserKindError(errors, request.UserKind, requestedUserKind);

        if (errors.Count > 0)
        {
            return Result<SuperAdminUserResponse>.Invalid(errors);
        }

        var filialId = request.FilialId ?? current.FilialId;
        if (!await repository.TenantFilialExistsAsync(
                current.OrganizationId,
                filialId,
                cancellationToken))
        {
            return Result<SuperAdminUserResponse>.Invalid([
                new ValidationError
                {
                    Identifier = nameof(request.FilialId),
                    ErrorMessage = "Den valgte filial tilhører ikke brugerens organisation."
                }
            ]);
        }

        var identity = await repository.GetByEmailAsync(
            current.Email.Trim().ToLowerInvariant(),
            cancellationToken);
        var displayName = request.DisplayName?.Trim() ?? current.DisplayName;
        var phone = request.Phone?.Trim() ?? current.Phone;
        var role = request.Role ?? current.Role;
        var updatedAt = DateTimeOffset.UtcNow;

        var updated = await repository.UpdateAsync(
            userId,
            displayName,
            phone,
            role,
            filialId,
            requestedUserKind!,
            updatedAt,
            cancellationToken);
        if (!updated)
        {
            return Result<SuperAdminUserResponse>.NotFound();
        }

        claimsCache.Invalidate(identity?.EntraId, current.Email, identity?.EntraEmail);

        var response = await repository.GetAsync(userId, cancellationToken);
        if (response is null)
        {
            return Result<SuperAdminUserResponse>.NotFound();
        }

        logger.LogInformation(
            "Superadmin updated tenant user. UserId: {UserId}. OrganizationId: {OrganizationId}. FilialId: {FilialId}. Role: {Role}. UserKind: {UserKind}.",
            response.Id,
            response.OrganizationId,
            response.FilialId,
            response.Role,
            response.UserKind);

        return Result<SuperAdminUserResponse>.Success(ToResponse(response));
    }

    public async Task<Result> DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!IsSuperadminActor())
        {
            return Result.Forbidden();
        }

        var current = await repository.GetAsync(userId, cancellationToken);
        if (current is null)
        {
            return Result.NotFound();
        }

        var identity = await repository.GetByEmailAsync(
            current.Email.Trim().ToLowerInvariant(),
            cancellationToken);
        var status = await repository.DeleteAsync(userId, cancellationToken);
        if (status == SuperAdminUserDeleteStatus.Deleted)
        {
            claimsCache.Invalidate(identity?.EntraId, current.Email, identity?.EntraEmail);
        }

        return status switch
        {
            SuperAdminUserDeleteStatus.Deleted => Result.NoContent(),
            SuperAdminUserDeleteStatus.NotFound => Result.NotFound(),
            SuperAdminUserDeleteStatus.HasHistory => Result.Conflict("user_has_history"),
            _ => Result.Error("superadmin_user_delete_failed")
        };
    }

    private bool IsSuperadminActor() =>
        string.Equals(currentUser.Role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase);

    private static SuperAdminUserResponse ToResponse(SuperAdminUserRecord user) => new(
        user.Id,
        user.OrganizationId,
        user.OrganizationName,
        user.FilialId,
        user.FilialName,
        user.Email,
        user.DisplayName,
        user.Phone,
        user.Role,
        user.UserKind,
        user.CreatedAt,
        user.UpdatedAt);

    private static List<ValidationError> MapValidationErrors(ValidationResult result) =>
        result.Errors
            .Select(error => new ValidationError
            {
                Identifier = error.PropertyName,
                ErrorMessage = error.ErrorMessage
            })
            .ToList();

    private static void AddTenantRoleError(ICollection<ValidationError> errors, string? role)
    {
        if (role is null || TenantRoles.Contains(role))
        {
            return;
        }

        errors.Add(new ValidationError
        {
            Identifier = nameof(SuperAdminCreateUserRequest.Role),
            ErrorMessage = "Tenant-brugere kan have rollen User, Admin eller Auditor."
        });
    }

    private static string? NormalizeRequestedUserKind(string? userKind) =>
        string.IsNullOrWhiteSpace(userKind)
            ? UserKinds.Member
            : UserKinds.Normalize(userKind);

    private static void AddUserKindError(
        ICollection<ValidationError> errors,
        string? requestedUserKind,
        string? normalizedUserKind)
    {
        if (requestedUserKind is null || normalizedUserKind is not null)
        {
            return;
        }

        errors.Add(new ValidationError
        {
            Identifier = nameof(SuperAdminCreateUserRequest.UserKind),
            ErrorMessage = "Brugergruppen skal være Member eller InternalTest."
        });
    }

    private async Task TryCompensateEntraCreateAsync(
        CreateEntraUserResult entraUser,
        CancellationToken cancellationToken)
    {
        if (!entraUser.Created || string.IsNullOrWhiteSpace(entraUser.EntraUserId))
        {
            return;
        }

        try
        {
            if (await repository.IsEntraIdentityReferencedAsync(entraUser.EntraUserId, cancellationToken))
            {
                return;
            }

            await entraService.DeleteUserAsync(entraUser.EntraUserId, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Superadmin user Entra compensation failed. EntraUserId: {EntraUserId}.",
                entraUser.EntraUserId);
        }
    }
}
