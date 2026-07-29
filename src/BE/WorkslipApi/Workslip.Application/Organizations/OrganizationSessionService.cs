using Ardalis.Result;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;
using Workslip.Application.Users;
using Workslip.Domain;

namespace Workslip.Application.Organizations;

public interface IOrganizationSessionService
{
    Task<Result<OrganizationSessionContext>> CreateAsync(
        Guid organizationId,
        CancellationToken cancellationToken);
}

public sealed class OrganizationSessionService(
    ICurrentUserContext currentUser,
    IUserRepository userRepository,
    IOrganizationAdministrationRepository organizationRepository,
    ILogger<OrganizationSessionService> logger) : IOrganizationSessionService
{
    public async Task<Result<OrganizationSessionContext>> CreateAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
        {
            return Result<OrganizationSessionContext>.Unauthorized();
        }

        if (!string.Equals(currentUser.Role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase))
        {
            return Result<OrganizationSessionContext>.Forbidden();
        }

        // EfUserRepository intentionally allows an authenticated user to load
        // their own row independent of the effective organization claim. This
        // preserves the real actor identity while tenant repositories continue
        // to use the delegated organization from the short-lived token.
        var actor = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (actor is null || !string.Equals(actor.Role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Delegated organization session rejected because the authenticated actor is no longer a Superadmin. UserId: {UserId}.",
                userId);
            return Result<OrganizationSessionContext>.Forbidden();
        }

        var organization = await organizationRepository.GetOrganizationAsync(organizationId, cancellationToken);
        if (organization is null)
        {
            logger.LogInformation(
                "Delegated organization session target was not found. UserId: {UserId}. OrganizationId: {OrganizationId}.",
                userId,
                organizationId);
            return Result<OrganizationSessionContext>.NotFound();
        }

        var response = new OrganizationSessionContext(
            new AuthUserInfo(
                actor.Id,
                organization.Id,
                actor.Email,
                actor.DisplayName,
                Roles.Superadmin),
            actor.OrganizationId,
            new OrganizationResponse(
                organization.Id,
                organization.Name,
                organization.Cvr,
                organization.CreatedAt,
                organization.UpdatedAt));

        logger.LogInformation(
            "Delegated organization session authorized. UserId: {UserId}. HomeOrganizationId: {HomeOrganizationId}. EffectiveOrganizationId: {EffectiveOrganizationId}.",
            actor.Id,
            actor.OrganizationId,
            organization.Id);

        return Result<OrganizationSessionContext>.Success(response);
    }
}
