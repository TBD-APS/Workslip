using Ardalis.Result;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;
using Workslip.Application.Common;
using Workslip.Application.Users;
using Workslip.Domain.Models;
using Workslip.Domain;

namespace Workslip.Application.Invitations;

public sealed class InvitationService(
    IUserRepository userRepository,
    IInviteRepository inviteRepository,
    IUserEntraService entraService,
    IApplicationTransactionFactory transactionFactory,
    IEmailService emailService,
    ICurrentUserContext currentUser,
    ILogger<InvitationService> logger) : IInvitationService
{
    private const string ExistingUserMismatchError = "invite_existing_user_mismatch";
    private const string RoleChangeRequiresStatusClearMessage =
        "Ryd den eksisterende invitationsstatus, før du sender en ny invitation med en anden rolle.";
    private const string AudienceChangeRequiresStatusClearMessage =
        "Ryd den eksisterende invitationsstatus, før invitationen flyttes til en anden brugergruppe.";

    public async Task<Result<InviteUsersResponse>> InviteUsersAsync(
        InviteUsersRequest request,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.OrganizationId;
        if (organizationId is null)
        {
            return Result<InviteUsersResponse>.Unauthorized();
        }

        var role = NormalizeInviteRole(request.Role);
        if (role is null)
        {
            return Result<InviteUsersResponse>.Invalid(new ValidationError
            {
                Identifier = nameof(InviteUsersRequest.Role),
                ErrorMessage = "Rollen skal være User eller Auditor."
            });
        }

        var userKind = await ResolveInvitationUserKindAsync(organizationId.Value, cancellationToken);
        if (userKind is null)
        {
            logger.LogWarning("Invite denied: authenticated actor audience could not be resolved.");
            return Result<InviteUsersResponse>.Unauthorized();
        }

        var results = new List<InviteUserResult>();
        foreach (var email in request.Emails)
        {
            var result = await ProcessInviteEmailAsync(
                email,
                organizationId.Value,
                role,
                userKind,
                cancellationToken);
            results.Add(result);
        }

        return Result<InviteUsersResponse>.Success(new InviteUsersResponse(results));
    }

    public async Task<Result<AuthUserInfo>> CompleteEnrollmentAsync(
        EntraEnrollRequest request,
        CancellationToken cancellationToken)
    {
        return await CompleteInviteAsync(request.Token, request.DisplayName, request.Phone, cancellationToken);
    }

    private async Task<Result<AuthUserInfo>> CompleteInviteAsync(
        string token,
        string displayName,
        string? phone,
        CancellationToken cancellationToken)
    {
        displayName = displayName.Trim();
        phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(displayName))
        {
            return Result<AuthUserInfo>.Invalid(new ValidationError
            {
                Identifier = string.IsNullOrWhiteSpace(token)
                    ? nameof(EntraEnrollRequest.Token)
                    : nameof(EntraEnrollRequest.DisplayName),
                ErrorMessage = "Invitationstoken og visningsnavn er påkrævet."
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
        {
            return validationError;
        }

        if (string.IsNullOrWhiteSpace(invite.EntraUserId))
        {
            logger.LogWarning(
                "Invite enrollment failed: Entra user not pre-provisioned. InviteId: {InviteId}",
                invite.Id);
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
                if (!ExistingUserMatchesInvite(existingUser, invite))
                {
                    logger.LogWarning(
                        "Invite enrollment blocked because the existing user does not match the invitation. InviteId: {InviteId}. UserId: {UserId}.",
                        invite.Id,
                        existingUser.Id);
                    await RollbackTransactionAsync(transaction, cancellationToken);
                    return Result<AuthUserInfo>.Conflict(ExistingUserMismatchError);
                }

                userId = existingUser.Id;
                organizationId = existingUser.OrganizationId;
                userRole = existingUser.Role;
                userDisplayName = existingUser.DisplayName;

                logger.LogInformation(
                    "Invite accepted for existing user. UserId: {UserId}. InviteId: {InviteId}",
                    userId,
                    invite.Id);
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

            logger.LogInformation(
                "Invite enrollment complete. UserId: {UserId}. OrganizationId: {OrganizationId}. InviteId: {InviteId}. Role: {Role}. UserKind: {UserKind}.",
                userId,
                organizationId,
                invite.Id,
                userRole,
                invite.UserKind);

            return Result<AuthUserInfo>.Success(
                new AuthUserInfo(userId, organizationId, invite.Email, userDisplayName, userRole));
        }
        catch (Exception ex)
        {
            await RollbackTransactionAsync(transaction, cancellationToken);
            logger.LogError(ex, "Invite enrollment failed. InviteId: {InviteId}", invite.Id);
            throw;
        }
    }

    private async Task RollbackTransactionAsync(
        IApplicationTransaction transaction,
        CancellationToken cancellationToken)
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

    private async Task<Result<AuthUserInfo>?> ValidateInviteAsync(
        InviteTokenRow invite,
        CancellationToken cancellationToken)
    {
        var validationError = await GetInviteValidationErrorAsync(invite, cancellationToken);
        return validationError is null
            ? null
            : Result<AuthUserInfo>.Conflict(validationError);
    }

    private async Task<InviteUserResult> ProcessInviteEmailAsync(
        string email,
        Guid organizationId,
        string role,
        string userKind,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return new InviteUserResult(email, false, "E-mailadressen er tom.", null);
        }

        var normalizedEmail = email.Trim().ToLowerInvariant();

        try
        {
            var token = Guid.NewGuid().ToString("N");
            var existingInvite = await inviteRepository.GetInviteByEmailAsync(
                organizationId,
                normalizedEmail,
                cancellationToken);
            Guid inviteId;

            if (existingInvite == null)
            {
                inviteId = Guid.NewGuid();
                var newInviteRow = new InviteTokenRow
                {
                    Id = inviteId,
                    OrganizationId = organizationId,
                    Email = normalizedEmail,
                    Token = token,
                    Role = role,
                    UserKind = userKind,
                    ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
                    Consumed = false,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                await inviteRepository.CreateAsync(newInviteRow, cancellationToken);
            }
            else
            {
                var existingRole = NormalizeInviteRole(existingInvite.Role)
                    ?? existingInvite.Role?.Trim();

                if (!string.Equals(existingRole, role, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(
                        "Invite role change blocked. InviteId: {InviteId}. OrganizationId: {OrganizationId}. ExistingRole: {ExistingRole}. RequestedRole: {RequestedRole}.",
                        existingInvite.Id,
                        organizationId,
                        existingRole,
                        role);

                    return new InviteUserResult(
                        normalizedEmail,
                        false,
                        RoleChangeRequiresStatusClearMessage,
                        null);
                }

                var existingUserKind = UserKinds.Normalize(existingInvite.UserKind);
                if (!string.Equals(existingUserKind, userKind, StringComparison.Ordinal))
                {
                    logger.LogWarning(
                        "Invite audience change blocked. InviteId: {InviteId}. OrganizationId: {OrganizationId}. ExistingUserKind: {ExistingUserKind}. RequestedUserKind: {RequestedUserKind}.",
                        existingInvite.Id,
                        organizationId,
                        existingInvite.UserKind,
                        userKind);

                    return new InviteUserResult(
                        normalizedEmail,
                        false,
                        AudienceChangeRequiresStatusClearMessage,
                        null);
                }

                inviteId = existingInvite.Id;
                existingInvite.ExpiresAt = DateTimeOffset.UtcNow.AddDays(7);
                existingInvite.Token = token;
                existingInvite.Role = role;
                existingInvite.UserKind = userKind;
                existingInvite.Consumed = false;
                await inviteRepository.UpdateAsync(existingInvite, cancellationToken);
            }

            await emailService.SendInviteEmailAsync(normalizedEmail, token, cancellationToken);
            logger.LogInformation(
                "Invite sent. InviteId: {InviteId}. OrganizationId: {OrganizationId}. Role: {Role}. UserKind: {UserKind}.",
                inviteId,
                organizationId,
                role,
                userKind);

            return new InviteUserResult(normalizedEmail, true, null, null);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Failed to send invite. OrganizationId: {OrganizationId}", organizationId);
            return new InviteUserResult(normalizedEmail, false, "Invitationen kunne ikke sendes.", null);
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

    public async Task<Result<InviteOpenResponse>> MarkOpenedAsync(
        string token,
        CancellationToken cancellationToken)
    {
        var invite = await inviteRepository.GetByTokenAsync(token, cancellationToken);

        if (invite is null)
        {
            logger.LogWarning("Unable to open invite because token was not found.");
            return Result<InviteOpenResponse>.NotFound();
        }

        if (invite.Consumed)
        {
            var consumedUser = await userRepository.GetByEmailAsync(invite.Email, cancellationToken);
            logger.LogInformation(
                "Invite re-opened after consumption. InviteId: {InviteId}. UserExists: {UserExists}",
                invite.Id,
                consumedUser is not null);
            return Result<InviteOpenResponse>.Success(
                new InviteOpenResponse(invite.Email, consumedUser is not null, Consumed: true));
        }

        var validationError = await ValidateInviteForOpenAsync(invite, cancellationToken);
        if (validationError is not null)
        {
            return Result<InviteOpenResponse>.Conflict(
                validationError.Errors.FirstOrDefault() ?? "validation_failed");
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
                logger.LogError(
                    ex,
                    "Invite open failed during Entra guest pre-creation. InviteId: {InviteId}",
                    invite.Id);
                throw;
            }
        }
        else
        {
            logger.LogDebug("Invite open reused provisioned Entra user. InviteId: {InviteId}", invite.Id);
        }

        await inviteRepository.MarkOpenedAsync(invite, cancellationToken);
        logger.LogInformation("Invite opened and Entra guest ensured. InviteId: {InviteId}", invite.Id);

        var user = await userRepository.GetByEmailAsync(invite.Email, cancellationToken);
        var userExists = user is not null;

        return Result<InviteOpenResponse>.Success(
            new InviteOpenResponse(invite.Email, userExists, Consumed: invite.Consumed));
    }

    private async Task<Result?> ValidateInviteForOpenAsync(
        InviteTokenRow invite,
        CancellationToken cancellationToken)
    {
        var validationError = await GetInviteValidationErrorAsync(invite, cancellationToken);
        return validationError is null
            ? null
            : Result.Conflict(validationError);
    }

    private Task<string?> GetInviteValidationErrorAsync(
        InviteTokenRow invite,
        CancellationToken cancellationToken)
    {
        if (invite.Consumed)
        {
            logger.LogWarning("Invite verification failed: already consumed. InviteId: {InviteId}", invite.Id);
            return Task.FromResult<string?>("invite_consumed");
        }

        if (DateTimeOffset.UtcNow > invite.ExpiresAt)
        {
            logger.LogWarning("Invite verification failed: expired. InviteId: {InviteId}", invite.Id);
            return Task.FromResult<string?>("invite_expired");
        }

        return Task.FromResult<string?>(null);
    }

    public async Task<int> CleanupStaleEntraInvitesAsync(
        DateTimeOffset now,
        int take,
        CancellationToken cancellationToken)
    {
        var staleInvites = await inviteRepository.GetStaleEntraProvisionedAsync(
            now,
            take,
            cancellationToken);
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
                logger.LogError(ex, "Failed to clean stale invite-owned Entra user. InviteId: {InviteId}", invite.Id);
            }
        }

        return cleanedCount;
    }

    private async Task MarkInviteEntraCleanedAsync(
        InviteTokenRow invite,
        CancellationToken cancellationToken)
    {
        invite.EntraCleanedAt = DateTimeOffset.UtcNow;
        await inviteRepository.UpdateAsync(invite, cancellationToken);
    }

    private async Task<string?> ResolveInvitationUserKindAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(currentUser.Role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase))
        {
            return UserKinds.Member;
        }

        if (currentUser.UserId is not Guid actorId)
        {
            return null;
        }

        var actor = await userRepository.GetAuthenticatedActorAsync(actorId, cancellationToken);
        if (actor?.OrganizationId != organizationId)
        {
            return null;
        }

        return UserKinds.Normalize(actor.UserKind);
    }

    private static string? NormalizeInviteRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return Roles.User;
        }

        var normalized = role.Trim();
        if (normalized.Equals(Roles.User, StringComparison.OrdinalIgnoreCase))
        {
            return Roles.User;
        }

        if (normalized.Equals(Roles.Auditor, StringComparison.OrdinalIgnoreCase))
        {
            return Roles.Auditor;
        }

        return null;
    }

    private static bool ExistingUserMatchesInvite(UserDataRow user, InviteTokenRow invite)
    {
        var existingRole = NormalizeInviteRole(user.Role);
        var inviteRole = NormalizeInviteRole(invite.Role);
        var existingUserKind = UserKinds.Normalize(user.UserKind);
        var inviteUserKind = UserKinds.Normalize(invite.UserKind);

        return user.OrganizationId == invite.OrganizationId
            && existingRole is not null
            && string.Equals(existingRole, inviteRole, StringComparison.Ordinal)
            && existingUserKind is not null
            && string.Equals(existingUserKind, inviteUserKind, StringComparison.Ordinal);
    }

    private static UserDataRow BuildUserFromInvite(
        InviteTokenRow invite,
        string displayName,
        string? phone) =>
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
            UserKind = UserKinds.Normalize(invite.UserKind) ?? UserKinds.Member,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
}
