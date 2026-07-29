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
    private const int MaxStateTransitionAttempts = 3;

    public async Task<Result> ClearAsync(Guid inviteId, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result.Unauthorized();
        }

        var inviteWasFound = false;

        for (var attempt = 0; attempt < MaxStateTransitionAttempts; attempt++)
        {
            var invite = await invitationRepository.GetByIdAsync(organizationId.Value, inviteId, cancellationToken);
            if (invite is null)
            {
                return inviteWasFound ? Result.Success() : Result.NotFound();
            }

            inviteWasFound = true;

            if (!invite.Consumed && invite.RevokedAt is null)
            {
                var revokedAt = DateTimeOffset.UtcNow;
                var replacementToken = Guid.NewGuid().ToString("N");
                var claimed = await invitationRepository.TryRevokePendingAsync(
                    organizationId.Value,
                    invite.Id,
                    invite.Token,
                    revokedAt,
                    replacementToken,
                    cancellationToken);

                if (!claimed)
                {
                    continue;
                }

                invite.RevokedAt = revokedAt;
                invite.ExpiresAt = revokedAt;
                invite.Token = replacementToken;
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

                var deleted = await invitationRepository.TryDeleteAsync(
                    organizationId.Value,
                    invite.Id,
                    invite.Token,
                    invite.Consumed,
                    invite.RevokedAt,
                    cancellationToken);

                if (!deleted)
                {
                    continue;
                }

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
                    "Invitation status clear failed. InviteId: {InviteId}. OrganizationId: {OrganizationId}. RevokedAt: {RevokedAt}",
                    invite.Id,
                    organizationId.Value,
                    invite.RevokedAt);
                return Result.Error("invite_status_clear_failed");
            }
        }

        logger.LogWarning(
            "Invitation status changed repeatedly while clearing. InviteId: {InviteId}. OrganizationId: {OrganizationId}",
            inviteId,
            organizationId.Value);
        return Result.Conflict("invite_status_changed");
    }
}
