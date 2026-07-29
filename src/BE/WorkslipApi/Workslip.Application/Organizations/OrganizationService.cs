using Ardalis.Result;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;

namespace Workslip.Application.Organizations;

public interface IOrganizationService
{
    Task<Result<OrganizationOnboardingResponse>> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken);

    Task<Result<OrganizationUserResponse>> UpsertAdminAsync(
        Guid organizationId,
        UpsertOrganizationAdminRequest request,
        CancellationToken cancellationToken);
}

public sealed class OrganizationService(
    IOrganizationRepository repository,
    IOrganizationAdministrationRepository administrationRepository,
    IValidator<CreateOrganizationRequest> createOrganizationValidator,
    IValidator<UpsertOrganizationAdminRequest> upsertAdminValidator,
    IUserEntraService entraService,
    ILogger<OrganizationService> logger) : IOrganizationService
{
    public async Task<Result<OrganizationOnboardingResponse>> CreateAsync(CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await createOrganizationValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = MapValidationErrors(validationResult);
            logger.LogWarning("Organization create validation failed. Fields: {ValidationFields}", ValidationFields(errors));

            return Result<OrganizationOnboardingResponse>.Invalid(errors);
        }

        var normalizedAdminEmail = NullIfWhiteSpace(request.AdminEmail)?.ToLowerInvariant();
        if (normalizedAdminEmail is not null)
        {
            var existingAdminEmail = await administrationRepository.GetUserByEmailAsync(normalizedAdminEmail, cancellationToken);
            if (existingAdminEmail is not null)
            {
                logger.LogWarning(
                    "Organization create conflict. ExistingOrganizationId: {ExistingOrganizationId}. UserId: {UserId}. Reason: {Reason}.",
                    existingAdminEmail.OrganizationId,
                    existingAdminEmail.Id,
                    "email_in_use");
                return Result<OrganizationOnboardingResponse>.Conflict("email_in_use");
            }
        }

        var normalizedCvr = OrganizationRequestValidator.NormalizeCvr(request.Cvr);
        if (await repository.CvrExistsAsync(normalizedCvr, cancellationToken))
        {
            logger.LogWarning("Organization create conflict. Reason: {Reason}. Cvr: {Cvr}.", "organization_cvr_exists", normalizedCvr);
            return Result<OrganizationOnboardingResponse>.Conflict("organization_cvr_exists");
        }

        var created = await repository.CreateAsync(request, normalizedCvr, cancellationToken);
        if (created is null)
        {
            logger.LogWarning("Organization create conflict after insert attempt. Cvr: {Cvr}.", normalizedCvr);

            return Result<OrganizationOnboardingResponse>.Conflict("organization_cvr_exists");
        }

        logger.LogInformation(
            "Organization created. OrganizationId: {OrganizationId}. UserId: {UserId}. Cvr: {Cvr}.",
            created.Organization.Id,
            created.User.Id,
            normalizedCvr);

        return Result<OrganizationOnboardingResponse>.Success(created);
    }

    public async Task<Result<OrganizationUserResponse>> UpsertAdminAsync(
        Guid organizationId,
        UpsertOrganizationAdminRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await upsertAdminValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = MapValidationErrors(validationResult);
            logger.LogWarning(
                "Organization admin upsert validation failed. OrganizationId: {OrganizationId}. Fields: {ValidationFields}",
                organizationId,
                ValidationFields(errors));

            return Result<OrganizationUserResponse>.Invalid(errors);
        }

        var organization = await administrationRepository.GetOrganizationAsync(organizationId, cancellationToken);
        if (organization is null)
        {
            logger.LogInformation("Organization not found for admin upsert. OrganizationId: {OrganizationId}.", organizationId);
            return Result<OrganizationUserResponse>.NotFound();
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var existingByEmail = await administrationRepository.GetUserByEmailAsync(normalizedEmail, cancellationToken);
        var initialConflict = GetAdminConflict(existingByEmail, organizationId);
        if (initialConflict is not null)
        {
            LogAdminConflict(organizationId, existingByEmail, initialConflict);
            return Result<OrganizationUserResponse>.Conflict(initialConflict);
        }

        var admin = existingByEmail
            ?? await administrationRepository.GetUnlinkedAdminAsync(organizationId, cancellationToken);

        var entraUser = await entraService.CreateUserAsync(normalizedEmail, request.DisplayName.Trim(), cancellationToken);
        AdminPersistenceResult persistenceResult;
        try
        {
            persistenceResult = await PersistAdminAsync(
                organizationId,
                normalizedEmail,
                request,
                entraUser,
                admin,
                cancellationToken);
        }
        catch
        {
            await TryRollbackCreatedEntraUserAsync(entraUser, cancellationToken);
            throw;
        }

        if (persistenceResult.Conflict is not null)
        {
            await TryRollbackCreatedEntraUserAsync(entraUser, cancellationToken);
            LogAdminConflict(organizationId, persistenceResult.ConflictingUser, persistenceResult.Conflict);
            return Result<OrganizationUserResponse>.Conflict(persistenceResult.Conflict);
        }

        var persistedAdmin = persistenceResult.Admin!;
        logger.LogInformation(
            "Organization admin upserted. OrganizationId: {OrganizationId}. UserId: {UserId}. Created: {Created}.",
            organizationId,
            persistedAdmin.Id,
            persistenceResult.Created);

        return Result<OrganizationUserResponse>.Success(ToOrganizationUserResponse(persistedAdmin));
    }

    private async Task<AdminPersistenceResult> PersistAdminAsync(
        Guid organizationId,
        string normalizedEmail,
        UpsertOrganizationAdminRequest request,
        CreateEntraUserResult entraUser,
        UserDataRow? admin,
        CancellationToken cancellationToken)
    {
        var currentEmailOwner = await administrationRepository.GetUserByEmailAsync(normalizedEmail, cancellationToken);
        var conflict = GetAdminConflict(currentEmailOwner, organizationId);
        if (conflict is not null)
        {
            return AdminPersistenceResult.FromConflict(conflict, currentEmailOwner);
        }

        if (currentEmailOwner is not null && currentEmailOwner.Id != admin?.Id)
        {
            admin = currentEmailOwner;
        }
        else if (admin is not null && string.IsNullOrWhiteSpace(admin.Email))
        {
            admin = await administrationRepository.GetUnlinkedAdminAsync(organizationId, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        if (admin is null)
        {
            var createdAdmin = BuildAdmin(
                Guid.NewGuid(),
                organizationId,
                normalizedEmail,
                request,
                entraUser,
                now,
                now);
            createdAdmin.Id = await administrationRepository.CreateAdminAsync(createdAdmin, cancellationToken);
            return AdminPersistenceResult.Success(createdAdmin, created: true);
        }

        var expectedEmail = admin.Email;
        var expectedEntraId = admin.EntraId;
        var updatedAdmin = BuildAdmin(
            admin.Id,
            admin.OrganizationId,
            normalizedEmail,
            request,
            entraUser,
            admin.CreatedAt == default ? now : admin.CreatedAt,
            now);

        var updated = await administrationRepository.UpdateAdminAsync(
            updatedAdmin,
            expectedEmail,
            expectedEntraId,
            cancellationToken);
        if (!updated)
        {
            var conflictingUser = await administrationRepository.GetUserByEmailAsync(normalizedEmail, cancellationToken);
            var stateConflict = GetAdminConflict(conflictingUser, organizationId) ?? "admin_state_changed";
            return AdminPersistenceResult.FromConflict(stateConflict, conflictingUser);
        }

        return AdminPersistenceResult.Success(updatedAdmin, created: false);
    }

    private static UserDataRow BuildAdmin(
        Guid id,
        Guid organizationId,
        string normalizedEmail,
        UpsertOrganizationAdminRequest request,
        CreateEntraUserResult entraUser,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt) =>
        new()
        {
            Id = id,
            OrganizationId = organizationId,
            Email = normalizedEmail,
            DisplayName = request.DisplayName.Trim(),
            Phone = NullIfWhiteSpace(request.Phone) ?? string.Empty,
            EntraId = entraUser.EntraUserId,
            EntraEmail = entraUser.EntraMail,
            Role = Roles.Admin,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };

    private async Task TryRollbackCreatedEntraUserAsync(CreateEntraUserResult entraUser, CancellationToken cancellationToken)
    {
        if (!entraUser.Created)
        {
            return;
        }

        try
        {
            if (await administrationRepository.IsEntraIdentityReferencedAsync(entraUser.EntraUserId, cancellationToken))
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
                "Organization admin Entra rollback failed. EntraUserId: {EntraUserId}.",
                entraUser.EntraUserId);
        }
    }

    private void LogAdminConflict(Guid organizationId, UserDataRow? existingUser, string conflict)
    {
        logger.LogWarning(
            "Organization admin upsert conflict. OrganizationId: {OrganizationId}. ExistingOrganizationId: {ExistingOrganizationId}. UserId: {UserId}. Reason: {Reason}.",
            organizationId,
            existingUser?.OrganizationId,
            existingUser?.Id,
            conflict);
    }

    private static string? GetAdminConflict(UserDataRow? existingUser, Guid organizationId)
    {
        if (existingUser is null)
        {
            return null;
        }

        if (existingUser.OrganizationId != organizationId)
        {
            return "email_in_use";
        }

        return existingUser.Role == Roles.Superadmin
            ? "superadmin_role_protected"
            : null;
    }

    private static OrganizationUserResponse ToOrganizationUserResponse(UserDataRow user) => new(
        user.Id,
        user.OrganizationId,
        user.DisplayName,
        NullIfWhiteSpace(user.Email),
        NullIfWhiteSpace(user.Phone),
        user.Role,
        user.CreatedAt,
        user.UpdatedAt);

    private static string ValidationFields(IEnumerable<ValidationError> errors) =>
        string.Join(",", errors.Select(error => error.Identifier).Distinct());

    private static List<ValidationError> MapValidationErrors(ValidationResult result) =>
        result.Errors
            .Select(error => new ValidationError { Identifier = error.PropertyName, ErrorMessage = error.ErrorMessage })
            .ToList();

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record AdminPersistenceResult(
        UserDataRow? Admin,
        string? Conflict,
        UserDataRow? ConflictingUser,
        bool Created)
    {
        public static AdminPersistenceResult Success(UserDataRow admin, bool created) =>
            new(admin, null, null, created);

        public static AdminPersistenceResult FromConflict(string conflict, UserDataRow? conflictingUser) =>
            new(null, conflict, conflictingUser, false);
    }
}
