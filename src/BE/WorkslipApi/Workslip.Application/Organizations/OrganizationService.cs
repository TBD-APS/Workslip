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
        if (existingByEmail is not null && existingByEmail.OrganizationId != organizationId)
        {
            logger.LogWarning(
                "Organization admin upsert conflict. OrganizationId: {OrganizationId}. ExistingOrganizationId: {ExistingOrganizationId}. Reason: {Reason}.",
                organizationId,
                existingByEmail.OrganizationId,
                "email_in_use");
            return Result<OrganizationUserResponse>.Conflict("email_in_use");
        }

        if (existingByEmail?.Role == Roles.Superadmin)
        {
            logger.LogWarning(
                "Organization admin upsert rejected for Superadmin account. OrganizationId: {OrganizationId}. UserId: {UserId}.",
                organizationId,
                existingByEmail.Id);
            return Result<OrganizationUserResponse>.Conflict("superadmin_role_protected");
        }

        var admin = existingByEmail
            ?? await administrationRepository.GetUnlinkedAdminAsync(organizationId, cancellationToken);

        var entraUser = await entraService.CreateUserAsync(normalizedEmail, request.DisplayName.Trim(), cancellationToken);
        try
        {
            admin = await PersistAdminAsync(organizationId, normalizedEmail, request, entraUser, admin, cancellationToken);
        }
        catch
        {
            await TryRollbackCreatedEntraUserAsync(entraUser, cancellationToken);
            throw;
        }

        logger.LogInformation(
            "Organization admin upserted. OrganizationId: {OrganizationId}. UserId: {UserId}. Created: {Created}.",
            organizationId,
            admin.Id,
            existingByEmail is null);

        return Result<OrganizationUserResponse>.Success(ToOrganizationUserResponse(admin));
    }

    private async Task<UserDataRow> PersistAdminAsync(
        Guid organizationId,
        string normalizedEmail,
        UpsertOrganizationAdminRequest request,
        CreateEntraUserResult entraUser,
        UserDataRow? admin,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (admin is null)
        {
            admin = new UserDataRow
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                CreatedAt = now
            };
        }

        admin.Email = normalizedEmail;
        admin.DisplayName = request.DisplayName.Trim();
        admin.Phone = NullIfWhiteSpace(request.Phone) ?? string.Empty;
        admin.EntraId = entraUser.EntraUserId;
        admin.EntraEmail = entraUser.EntraMail;
        admin.Role = Roles.Admin;
        admin.UpdatedAt = now;

        if (admin.CreatedAt == default)
        {
            admin.CreatedAt = now;
        }

        var existingAdmin = await administrationRepository.GetUserByEmailAsync(normalizedEmail, cancellationToken);
        if (existingAdmin is not null && existingAdmin.Id != admin.Id)
        {
            if (existingAdmin.OrganizationId != organizationId)
            {
                throw new InvalidOperationException("Admin email became assigned to another organization during upsert.");
            }

            if (existingAdmin.Role == Roles.Superadmin)
            {
                throw new InvalidOperationException("Superadmin account cannot be converted to an organization admin.");
            }

            admin = existingAdmin;
            admin.DisplayName = request.DisplayName.Trim();
            admin.Phone = NullIfWhiteSpace(request.Phone) ?? string.Empty;
            admin.EntraId = entraUser.EntraUserId;
            admin.EntraEmail = entraUser.EntraMail;
            admin.Role = Roles.Admin;
            admin.UpdatedAt = now;
        }

        var updated = await administrationRepository.UpdateAdminAsync(admin, cancellationToken);
        if (!updated)
        {
            admin.Id = await administrationRepository.CreateAdminAsync(admin, cancellationToken);
        }

        return admin;
    }

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
}
