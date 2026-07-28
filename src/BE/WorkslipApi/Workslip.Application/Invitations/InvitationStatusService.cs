using Ardalis.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models.ODataErrors;
using Workslip.Application.Auth;
using Workslip.Application.Users;

namespace Workslip.Application.Invitations;

public sealed class InvitationStatusService(
    IInvitationStatusRepository invitationRepository,
    IUserEntraService entraService,
    ICurrentUserContext currentUser,
    ILogger<InvitationStatusService> logger) : IInvitationStatusService
{
    public async Task<Result> ClearAsync(Guid inviteId, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result.Unauthorized();
        }

        var invite = await invitationRepository.GetByIdAsync(organizationId.Value, inviteId, cancellationToken);
        if (invite is null)
        {
            return Result.NotFound();
        }

        try
        {
            if (!invite.Consumed
                && invite.EntraCreatedByInvite
                && invite.EntraCleanedAt is null
                && !string.IsNullOrWhiteSpace(invite.EntraUserId))
            {
                try
                {
                    await entraService.DeleteUserAsync(invite.EntraUserId, cancellationToken);
                }
                catch (ODataError error) when (error.ResponseStatusCode == 404)
                {
                    logger.LogInformation(
                        "Invitation-owned Entra user was already removed. InviteId: {InviteId}. OrganizationId: {OrganizationId}",
                        invite.Id,
                        organizationId.Value);
                }
            }

            await invitationRepository.DeleteAsync(invite, cancellationToken);
            logger.LogInformation(
                "Invitation status cleared. InviteId: {InviteId}. OrganizationId: {OrganizationId}",
                invite.Id,
                organizationId.Value);

            return Result.Success();
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Invitation status clear failed. InviteId: {InviteId}. OrganizationId: {OrganizationId}",
                invite.Id,
                organizationId.Value);
            return Result.Error("invite_status_clear_failed");
        }
    }
}
