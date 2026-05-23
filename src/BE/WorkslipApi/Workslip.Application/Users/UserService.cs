using FluentValidation;
using Microsoft.Extensions.Logging;
using Workslip.Application.Auth;
using Workslip.Application.Organizations;
using Workslip.Domain.Models;

namespace Workslip.Application.Users;

public sealed class UserService(
    IUserRepository repository,
    IInviteRepository inviteRepository,
    IValidator<CreateUserRequest> createUserValidator,
    IValidator<UpdateUserRequest> updateUserValidator,
    IUserEntraService entraService,
    IEmailService emailService,
    IOrganizationRepository organizationRepository,
    ILogger<UserService> logger) : IUserService
{
    public async Task<(bool Success, UserResponse? User, IReadOnlyList<string>? Errors)> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var validationResult = await createUserValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors.Select(e => e.ErrorMessage).ToList();
            logger.LogWarning("User create validation failed. Errors: {Errors}", string.Join(", ", errors));
            return (false, null, errors);
        }

        var existing = await repository.GetByEmailAsync(request.Email, cancellationToken);
        if (existing != null)
            return (false, null, ["Email already in use"]);

        var entraUser = await entraService.CreateUserAsync(request.Email, request.DisplayName, cancellationToken);
        await entraService.AssignAppRoleTo(entraUser.EntraUserId, "Admin", cancellationToken);

        var user = new UserDataRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrganizationId,
            Email = request.Email,
            DisplayName = request.DisplayName,
            EntraEmail = entraUser.EntraMail,
            EntraId = entraUser.EntraUserId,
            Phone = request.Phone,
            Role = request.Role,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var userId = await repository.CreateAsync(user, cancellationToken);
        user.Id = userId;

        logger.LogInformation("User created. UserId: {UserId}. OrganizationId: {OrganizationId}. Role: {Role}.", user.Id, user.OrganizationId, user.Role);

        return (true, MapToResponse(user), null);
    }

    public async Task<(bool Success, UserResponse? User, IReadOnlyList<string>? Errors)> GetAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await repository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
            return (false, null, ["User not found"]);

        return (true, MapToResponse(user), null);
    }

    public async Task<(bool Success, UserListResponse? Users, IReadOnlyList<string>? Errors)> GetByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var users = await repository.GetByOrganizationIdAsync(organizationId, cancellationToken);
        var count = await repository.GetCountByOrganizationIdAsync(organizationId, cancellationToken);

        var responses = users.Select(MapToResponse).ToList();
        return (true, new UserListResponse(responses, count), null);
    }

    public async Task<(bool Success, UserResponse? User, IReadOnlyList<string>? Errors)> UpdateAsync(
        Guid userId,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await updateUserValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors.Select(e => e.ErrorMessage).ToList();
            logger.LogWarning("User update validation failed. Errors: {Errors}", string.Join(", ", errors));
            return (false, null, errors);
        }

        var user = await repository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
            return (false, null, ["User not found"]);

        if (!string.IsNullOrEmpty(request.DisplayName))
            user.DisplayName = request.DisplayName;

        if (!string.IsNullOrEmpty(request.Phone))
            user.Phone = request.Phone;

        if (!string.IsNullOrEmpty(request.Role))
            user.Role = request.Role;

        user.UpdatedAt = DateTimeOffset.UtcNow;

        await repository.UpdateAsync(user, cancellationToken);

        logger.LogInformation("User updated. UserId: {UserId}.", userId);

        return (true, MapToResponse(user), null);
    }

    public async Task<(bool Success, IReadOnlyList<string>? Errors)> DeleteAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await repository.GetByIdAsync(userId, cancellationToken);
        if (user == null)
            return (false, ["User not found"]);

        await repository.DeleteAsync(userId, cancellationToken);

        logger.LogInformation("User deleted. UserId: {UserId}.", userId);

        return (true, null);
    }

    public async Task<InviteUsersResponse> InviteUsersAsync(InviteUsersRequest request, CancellationToken cancellationToken)
    {
        var results = new List<InviteUserResult>();

        foreach (var email in request.Emails)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                results.Add(new InviteUserResult(email, false, "Email address is empty.", null));
                continue;
            }

            var existing = await repository.GetByEmailAsync(email, cancellationToken);
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
                OrganizationId = request.OrganizationId,
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

        return new InviteUsersResponse(results);
    }

    public async Task<AuthUserInfo?> VerifyInviteAsync(VerifyInviteRequest request, CancellationToken cancellationToken)
    {
        var invite = await inviteRepository.GetByTokenAsync(request.Token, cancellationToken);
        if (invite is null)
        {
            logger.LogWarning("Invite verification failed: token not found.");
            return null;
        }

        if (invite.Consumed)
        {
            logger.LogWarning("Invite verification failed: already consumed. Token: {Token}", invite.Token);
            return null;
        }

        if (DateTimeOffset.UtcNow > invite.ExpiresAt)
        {
            logger.LogWarning("Invite verification failed: expired. Token: {Token}", invite.Token);
            return null;
        }

        var existing = await repository.GetByEmailAsync(invite.Email, cancellationToken);
        if (existing is not null)
        {
            logger.LogWarning("Invite verification failed: user already exists. Email: {Email}", invite.Email);
            return null;
        }

        var nickName = invite.Email.Split('@')[0];
        var entraUser = await entraService.CreateUserAsync(invite.Email, nickName, cancellationToken);
        await entraService.AssignAppRoleTo(entraUser.EntraUserId, "User", cancellationToken);
        
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

        var userId = await repository.CreateAsync(user, cancellationToken);
        await inviteRepository.MarkConsumedAsync(invite.Id, cancellationToken);

        var org = await organizationRepository.GetByIdAsync(invite.OrganizationId, cancellationToken);

        logger.LogInformation("Invite accepted. UserId: {UserId}. Organization: {Org}. Email: {Email}. Role: {Role}.",
            userId, org?.Name ?? invite.OrganizationId.ToString(), invite.Email, user.Role);

        return new AuthUserInfo(userId, invite.OrganizationId, invite.Email, user.DisplayName, user.Role);
    }

    private static UserResponse MapToResponse(UserDataRow user) =>
        new(
            user.Id,
            user.OrganizationId,
            user.Email,
            user.DisplayName,
            user.Phone,
            user.Role,
            user.CreatedAt,
            user.UpdatedAt);
}
