using Ardalis.Result;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;
using Workslip.Application.Organizations;
using Workslip.Application.Users;
using Workslip.Domain.Models;

namespace Workslip.Application.Invitations;

public sealed class InvitationService(
    IUserRepository userRepository,
    IInviteRepository inviteRepository,
    IUserEntraService entraService,
    IOrganizationRepository organizationRepository,
    IEmailService emailService,
    ICurrentUserContext currentUser,
    ILogger<InvitationService> logger) : IInvitationService
{
    public async Task<Result<InviteUsersResponse>> InviteUsersAsync(InviteUsersRequest request, CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<InviteUsersResponse>.Unauthorized();
        }

        var results = new List<InviteUserResult>();

        foreach (var email in request.Emails)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                results.Add(new InviteUserResult(email, false, "Email address is empty.", null));
                continue;
            }

            var existing = await userRepository.GetByEmailAsync(email, cancellationToken);
            if (existing != null)
            {
                results.Add(new InviteUserResult(email, false, "User already exists.", null));
                continue;
            }

            var token = Guid.NewGuid().ToString("N");
            var inviteLink = $"{request.InviteBaseUrl}/{token}";

            var inviteRow = new InviteTokenRow
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId.Value,
                Email = email,
                Token = token,
                Role = request.Role,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                Consumed = false,
                CreatedAt = DateTimeOffset.UtcNow
            };

            try
            {
                await inviteRepository.CreateAsync(inviteRow, cancellationToken);
                await emailService.SendInviteEmailAsync(email, inviteLink, cancellationToken);
                results.Add(new InviteUserResult(email, true, null, inviteLink));
                logger.LogInformation("Invite sent to {Email}. Token: {Token}", email, token);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogError(ex, "Failed to send invite to {Email}.", email);
                results.Add(new InviteUserResult(email, false, "Failed to send invite email.", null));
            }
        }

        return Result<InviteUsersResponse>.Success(new InviteUsersResponse(results));
    }

    public async Task<Result<AuthUserInfo>> VerifyInviteAsync(VerifyInviteRequest request, CancellationToken cancellationToken)
    {
        var invite = await inviteRepository.GetByTokenAsync(request.Token, cancellationToken);
        if (invite is null)
        {
            logger.LogWarning("Invite verification failed: token not found.");
            return Result<AuthUserInfo>.NotFound();
        }

        if (invite.Consumed)
        {
            logger.LogWarning("Invite verification failed: already consumed. Token: {Token}", invite.Token);
            return Result<AuthUserInfo>.Conflict("invite_consumed");
        }

        if (DateTimeOffset.UtcNow > invite.ExpiresAt)
        {
            logger.LogWarning("Invite verification failed: expired. Token: {Token}", invite.Token);
            return Result<AuthUserInfo>.Conflict("invite_expired");
        }

        var existing = await userRepository.GetByEmailAsync(invite.Email, cancellationToken);
        if (existing is not null)
        {
            logger.LogWarning("Invite verification failed: user already exists. Email: {Email}", invite.Email);
            return Result<AuthUserInfo>.Conflict("user_already_exists");
        }

        var nickName = invite.Email.Split('@')[0];
        var entraUser = await entraService.CreateUserAsync(invite.Email, nickName, cancellationToken);
        
        var user = new UserDataRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = invite.OrganizationId,
            Email = invite.Email,
            DisplayName = nickName,
            EntraEmail = entraUser.EntraMail,
            EntraId = entraUser.EntraUserId,
            Role = invite.Role ?? "User",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var userId = await userRepository.CreateAsync(user, cancellationToken);
        await inviteRepository.MarkConsumedAsync(invite.Id, cancellationToken);

        var org = await organizationRepository.GetByIdAsync(invite.OrganizationId, cancellationToken);

        logger.LogInformation("Invite accepted. UserId: {UserId}. Organization: {Org}. Email: {Email}. Role: {Role}.",
            userId, org?.Name ?? invite.OrganizationId.ToString(), invite.Email, user.Role);

        return Result<AuthUserInfo>.Success(new AuthUserInfo(userId, invite.OrganizationId, invite.Email, user.DisplayName, user.Role));
    }
}
