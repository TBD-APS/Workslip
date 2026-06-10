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
            var result = await ProcessInviteEmailAsync(email, organizationId.Value, request.Role, request.InviteBaseUrl, cancellationToken);
            results.Add(result);
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

        var validationError = await ValidateInviteAsync(invite, cancellationToken);
        if (validationError is not null) return validationError;

        var entraUser = await entraService.CreateUserAsync(invite.Email, invite.Email.Split('@')[0], cancellationToken);

        var user = BuildUserFromInvite(invite, entraUser);
        var userId = await userRepository.CreateAsync(user, cancellationToken);
        await inviteRepository.MarkConsumedAsync(invite.Id, cancellationToken);

        var org = await organizationRepository.GetByIdAsync(invite.OrganizationId, cancellationToken);
        logger.LogInformation("Invite accepted. UserId: {UserId}. Organization: {Org}. Email: {Email}. Role: {Role}.",
            userId, org?.Name ?? invite.OrganizationId.ToString(), invite.Email, user.Role);

        return Result<AuthUserInfo>.Success(new AuthUserInfo(userId, invite.OrganizationId, invite.Email, user.DisplayName, user.Role));
    }

    private async Task<Result<AuthUserInfo>?> ValidateInviteAsync(InviteTokenRow invite, CancellationToken cancellationToken)
    {
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

        return null;
    }

    private async Task<InviteUserResult> ProcessInviteEmailAsync(string email, Guid organizationId, string? role, string inviteBaseUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
            return new InviteUserResult(email, false, "Email address is empty.", null);

        var existing = await userRepository.GetByEmailAsync(email, cancellationToken);
        if (existing != null)
            return new InviteUserResult(email, false, "User already exists.", null);

        var token = Guid.NewGuid().ToString("N");
        var inviteLink = $"{inviteBaseUrl}/{token}";

        var inviteRow = new InviteTokenRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Email = email,
            Token = token,
            Role = role,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
            Consumed = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        try
        {
            await inviteRepository.CreateAsync(inviteRow, cancellationToken);
            await emailService.SendInviteEmailAsync(email, inviteLink, cancellationToken);
            logger.LogInformation("Invite sent to {Email}. Token: {Token}", email, token);
            return new InviteUserResult(email, true, null, inviteLink);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Failed to send invite to {Email}.", email);
            return new InviteUserResult(email, false, "Failed to send invite email.", null);
        }
    }

    public async Task<Result<InviteListResponse>> GetOrganizationInvitesAsync(CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<InviteListResponse>.Unauthorized();
        }

        var invites = await inviteRepository.GetByOrganizationAsync(organizationId.Value, cancellationToken);

        var response = new InviteListResponse(
            invites.Select(i => new InviteTokenResponse(
                i.Id,
                i.Email,
                i.Role,
                i.CreatedAt,
                i.ExpiresAt,
                i.Consumed,
                i.OpenedAt,
                i.AcceptedAt
            )).ToList());

        return Result<InviteListResponse>.Success(response);
    }

    public async Task<Result> MarkOpenedAsync(Guid id, CancellationToken cancellationToken)
    {
        await inviteRepository.MarkOpenedAsync(id, cancellationToken);
        return Result.Success();
    }

    private static UserDataRow BuildUserFromInvite(InviteTokenRow invite, CreateEntraUserResult entraUser) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = invite.OrganizationId,
            Email = invite.Email,
            DisplayName = invite.Email.Split('@')[0],
            EntraEmail = entraUser.EntraMail,
            EntraId = entraUser.EntraUserId,
            Role = invite.Role ?? "User",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
}
