using Ardalis.Result;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;
using Workslip.Application.Common;
using Workslip.Application.Organizations;
using Workslip.Application.Users;
using Workslip.Domain.Models;
using Workslip.Domain;

namespace Workslip.Application.Invitations;

public sealed class InvitationService(
    IUserRepository userRepository,
    IInviteRepository inviteRepository,
    IUserEntraService entraService,
    IApplicationTransactionFactory transactionFactory,
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

    public async Task<Result<AuthUserInfo>> CompleteEnrollmentAsync(EntraEnrollRequest request, CancellationToken cancellationToken)
    {
        return await CompleteInviteAsync(request.Token, request.DisplayName, request.Phone, cancellationToken);
    }

    private async Task<Result<AuthUserInfo>> CompleteInviteAsync(string token, string displayName, string? phone, CancellationToken cancellationToken)
    {
        displayName = displayName.Trim();
        phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(displayName))
        {
            return Result<AuthUserInfo>.Invalid(new ValidationError
            {
                Identifier = string.IsNullOrWhiteSpace(token) ? nameof(EntraEnrollRequest.Token) : nameof(EntraEnrollRequest.DisplayName),
                ErrorMessage = "Invite token and display name are required."
            });
        }

        var invite = await inviteRepository.GetByTokenAsync(token, cancellationToken);
        if (invite is null)
        {
            logger.LogWarning("Invite verification failed: token not found.");
            return Result<AuthUserInfo>.NotFound();
        }

        var validationError = await ValidateInviteAsync(invite, cancellationToken);
        if (validationError is not null) 
            return validationError;

        if (string.IsNullOrWhiteSpace(invite.EntraUserId))
        {
            logger.LogWarning("Invite enrollment failed: Entra user not pre-provisioned. Token: {Token}", invite.Token);
            return Result<AuthUserInfo>.Conflict("entra_user_not_provisioned");
        }

        await using var transaction = await transactionFactory.BeginTransactionAsync(cancellationToken);

        try
        {
            var existingUser = await userRepository.GetByEmailAsync(invite.Email, cancellationToken);
            Guid userId;
            Guid organizationId;
            string userRole;
            string userDisplayName;

            if (existingUser is not null)
            {
                userId = existingUser.Id;
                organizationId = existingUser.OrganizationId;
                userRole = existingUser.Role;
                userDisplayName = existingUser.DisplayName;

                // Optionally update existing user with provided info if they haven't set it?
                // For now, just proceed with existing user.
                logger.LogInformation("Invite accepted for existing user. UserId: {UserId}. Email: {Email}. Token: {Token}", userId, invite.Email, invite.Token);
            }
            else
            {
                var user = BuildUserFromInvite(invite, displayName, phone);
                userId = await userRepository.CreateAsync(user, cancellationToken);
                organizationId = user.OrganizationId;
                userRole = user.Role;
                userDisplayName = user.DisplayName;
            }

            await inviteRepository.MarkConsumedAsync(invite, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var org = await organizationRepository.GetByIdAsync(organizationId, cancellationToken);
            logger.LogInformation("Invite enrollment complete. UserId: {UserId}. Organization: {Org}. Email: {Email}. Role: {Role}.",
                userId, org?.Name ?? organizationId.ToString(), invite.Email, userRole);

            return Result<AuthUserInfo>.Success(new AuthUserInfo(userId, organizationId, invite.Email, userDisplayName, userRole));
        }
        catch (Exception ex)
        {
            await RollbackTransactionAsync(transaction, cancellationToken);
            logger.LogError(ex, "Invite enrollment failed. Email: {Email}. Token: {Token}", invite.Email, invite.Token);
            throw;
        }
    }

    private async Task RollbackTransactionAsync(IApplicationTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            await transaction.RollbackAsync(cancellationToken);
        }
        catch (Exception rollbackException)
        {
            logger.LogError(rollbackException, "SQL transaction rollback failed during invite enrollment.");
        }
    }

    private async Task<Result<AuthUserInfo>?> ValidateInviteAsync(InviteTokenRow invite, CancellationToken cancellationToken)
    {
        var validationError = await GetInviteValidationErrorAsync(invite, cancellationToken);
        return validationError is null
            ? null
            : Result<AuthUserInfo>.Conflict(validationError);
    }

    private async Task<InviteUserResult> ProcessInviteEmailAsync(string email, Guid organizationId, string? role, string inviteBaseUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
            return new InviteUserResult(email, false, "Email address is empty.", null);

        var normalizedEmail = email.Trim().ToLowerInvariant();

        try
        {
            var token = Guid.NewGuid().ToString("N");
            var existingInvite = await inviteRepository.GetInviteByEmailAsync(organizationId, normalizedEmail, cancellationToken);

            if(existingInvite == null)
            {
                var newInviteRow = new InviteTokenRow
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    Email = normalizedEmail,
                    Token = token,
                    Role = role,
                    ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                    Consumed = false,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                await inviteRepository.CreateAsync(newInviteRow, cancellationToken);
            }
            else
            {
                existingInvite.ExpiresAt = DateTimeOffset.UtcNow.AddDays(7);
                existingInvite.Token = token;
                existingInvite.Consumed = false;
                await inviteRepository.UpdateAsync(existingInvite, cancellationToken);
            }

                await emailService.SendInviteEmailAsync(normalizedEmail, token, cancellationToken);
                logger.LogInformation("Invite sent to {Email}. Token: {Token}", normalizedEmail, token);
                return new InviteUserResult(normalizedEmail, true, null, token);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Failed to send invite to {Email}.", normalizedEmail);
            return new InviteUserResult(normalizedEmail, false, "Failed to send invite email.", null);
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
                i.AcceptedAt,
                i.EntraUserId,
                i.EntraCreatedByInvite,
                i.EntraProvisionedAt,
                i.EntraCleanedAt)).ToList());

        return Result<InviteListResponse>.Success(response);
    }

    public async Task<Result<InviteOpenResponse>> MarkOpenedAsync(string token, CancellationToken cancellationToken)
    {
        var invite = await inviteRepository.GetByTokenAsync(token, cancellationToken);

        if (invite is null)
        {
            logger.LogWarning("Unable to open invite because token was not found. Token: {Token}", token);
            return Result<InviteOpenResponse>.NotFound();
        }

        if (invite.Consumed)
        {
            var consumedUser = await userRepository.GetByEmailAsync(invite.Email, cancellationToken);
            logger.LogInformation("Invite re-opened after consumption. Token: {Token}. Email: {Email}. UserExists: {UserExists}",
                token, invite.Email, consumedUser is not null);
            return Result<InviteOpenResponse>.Success(new InviteOpenResponse(invite.Email, consumedUser is not null, Consumed: true));
        }

        var validationError = await ValidateInviteForOpenAsync(invite, cancellationToken);
        if (validationError is not null)
        {
            return Result<InviteOpenResponse>.Conflict(validationError.Errors.FirstOrDefault() ?? "validation_failed");
        }

        if (string.IsNullOrWhiteSpace(invite.EntraUserId))
        {
            try
            {
                var entraUser = await entraService.EnsureInvitedUserAsync(invite.Email, cancellationToken);
                invite.EntraUserId = entraUser.EntraUserId;
                invite.EntraEmail = entraUser.EntraMail;
                invite.EntraCreatedByInvite = entraUser.Created;
                invite.EntraProvisionedAt ??= DateTimeOffset.UtcNow;
                invite.EntraCleanedAt = null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Invite open failed during Entra guest pre-creation. Email: {Email}. Token: {Token}", invite.Email, invite.Token);
                throw;
            }
        }
        else
        {
            logger.LogError("Invite open reusing cached EntraUserId. Email: {Email}. Token: {Token}. EntraUserId: {EntraUserId}", invite.Email, invite.Token, invite.EntraUserId);
        }

        await inviteRepository.MarkOpenedAsync(invite, cancellationToken);
        logger.LogInformation("Invite opened and Entra guest ensured. Token: {Token}. Email: {Email}", token, invite.Email);

        var user = await userRepository.GetByEmailAsync(invite.Email, cancellationToken);
        var userExists = user is not null;

        return Result<InviteOpenResponse>.Success(new InviteOpenResponse(invite.Email, userExists, Consumed: invite.Consumed));
    }

    private async Task<Result?> ValidateInviteForOpenAsync(InviteTokenRow invite, CancellationToken cancellationToken)
    {
        var validationError = await GetInviteValidationErrorAsync(invite, cancellationToken);
        return validationError is null
            ? null
            : Result.Conflict(validationError);
    }

    private async Task<string?> GetInviteValidationErrorAsync(InviteTokenRow invite, CancellationToken cancellationToken)
    {
        if (invite.Consumed)
        {
            logger.LogWarning("Invite verification failed: already consumed. Token: {Token}", invite.Token);
            return "invite_consumed";
        }

        if (DateTimeOffset.UtcNow > invite.ExpiresAt)
        {
            logger.LogWarning("Invite verification failed: expired. Token: {Token}", invite.Token);
            return "invite_expired";
        }

        return null;
    }


    public async Task<int> CleanupStaleEntraInvitesAsync(DateTimeOffset now, int take, CancellationToken cancellationToken)
    {
        var staleInvites = await inviteRepository.GetStaleEntraProvisionedAsync(now, take, cancellationToken);
        var cleanedCount = 0;

        foreach (var invite in staleInvites)
        {
            var existingUser = await userRepository.GetByEmailAsync(invite.Email, cancellationToken);
            if (existingUser is not null || string.IsNullOrWhiteSpace(invite.EntraUserId))
            {
                continue;
            }

            try
            {
                await entraService.DeleteUserAsync(invite.EntraUserId, cancellationToken);
                await MarkInviteEntraCleanedAsync(invite, cancellationToken);
                cleanedCount++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to clean stale invite-owned Entra user. InviteId: {InviteId}. EntraUserId: {EntraUserId}", invite.Id, invite.EntraUserId);
            }
        }

        return cleanedCount;
    }

    private async Task MarkInviteEntraCleanedAsync(InviteTokenRow invite, CancellationToken cancellationToken)
    {
        invite.EntraCleanedAt = DateTimeOffset.UtcNow;
        await inviteRepository.UpdateAsync(invite, cancellationToken);
    }

    private static UserDataRow BuildUserFromInvite(InviteTokenRow invite, string displayName, string? phone) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = invite.OrganizationId,
            Email = invite.Email.Trim().ToLowerInvariant(),
            DisplayName = displayName,
            Phone = phone ?? string.Empty,
            EntraEmail = invite.EntraEmail ?? invite.Email,
            EntraId = invite.EntraUserId ?? string.Empty,
            Role = invite.Role ?? Roles.User,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
}
