using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

namespace Workslip.Application.Users;

public sealed class UserEntraService(
    ILogger<UserEntraService> logger,
    GraphServiceClient graphClient,
    IConfiguration configuration,
    ICorrelationIdAccessor correlationIdAccessor) : IUserEntraService
{
    public async Task<CreateEntraUserResult> CreateUserAsync(string email, string displayName, CancellationToken ct)
    {
        var user = await EnsureInvitedUserAsync(email, ct);
        return user with { DisplayName = displayName };
    }

    public async Task<CreateEntraUserResult> EnsureInvitedUserAsync(string email, CancellationToken ct)
    {
        var existingUser = await FindExistingEntraUserAsync(email, ct);
        if (existingUser != null)
        {
            await AssignAppRoleTo(existingUser.Id!, "User", ct);
            return new CreateEntraUserResult(existingUser.Id!, ResolveEntraMail(existingUser, email), existingUser.DisplayName ?? email, Created: false);
        }

        var redirectUrl = configuration["Azure:AdOAuth:InviteRedirectUri"]
            ?? configuration["Azure:AdOAuth:LoginRedirectUri"];

        var invitation = new Invitation
        {
            InvitedUserEmailAddress = email,
            InviteRedirectUrl = redirectUrl,
            SendInvitationMessage = false,
            InvitedUserDisplayName = email
        };

        var createdInvitation = await CreateExternalInviteAsync(invitation, email, ct);
        var invitedUser = createdInvitation.InvitedUser
            ?? throw new InvalidOperationException($"Graph did not return invited user for {email}.");

        try
        {
            await AssignAppRoleTo(invitedUser.Id!, "User", ct);
        }
        catch
        {
            await DeleteUserAsync(invitedUser.Id!, ct);
            throw;
        }

        return new CreateEntraUserResult(invitedUser.Id!, ResolveEntraMail(invitedUser, email), email, Created: true);
    }

    public async Task DeleteUserAsync(string entraUserId, CancellationToken ct)
    {
        logger.LogWarning("Graph deleting rollback user. CorrelationId={CorrelationId} UserId={UserId}",
            correlationIdAccessor.CorrelationId, entraUserId);

        try
        {
            await graphClient.Users[entraUserId].DeleteAsync(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Graph rollback delete failed. CorrelationId={CorrelationId} UserId={UserId}",
                correlationIdAccessor.CorrelationId, entraUserId);
            throw;
        }

        logger.LogInformation("Graph rollback user deleted. CorrelationId={CorrelationId} UserId={UserId}",
            correlationIdAccessor.CorrelationId, entraUserId);
    }

    public async Task AssignAppRoleTo(string entraUserId, string appRoleValue, CancellationToken ct)
    {
        var appId = configuration["Azure:AdOAuth:ClientId"];
        var servicePrincipal = await FetchServicePrincipalAsync(appId, ct);
        var appRole = FindAppRole(servicePrincipal, appRoleValue);

        logger.LogInformation("Graph assigning app role. CorrelationId={CorrelationId} UserId={UserId} Role={Role}",
            correlationIdAccessor.CorrelationId, entraUserId, appRoleValue);

        try
        {
            await graphClient.Users[entraUserId].AppRoleAssignments.PostAsync(
                new AppRoleAssignment
                {
                    PrincipalId = Guid.Parse(entraUserId),
                    ResourceId = Guid.Parse(servicePrincipal.Id!),
                    AppRoleId = appRole.Id!.Value
                },
                cancellationToken: ct
            );
        }
        catch (ODataError ex) when (IsDuplicateAppRoleAssignment(ex))
        {
            logger.LogInformation("Graph app role already assigned. CorrelationId={CorrelationId} UserId={UserId} Role={Role}",
                correlationIdAccessor.CorrelationId, entraUserId, appRoleValue);
            return;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Graph assign app role failed. CorrelationId={CorrelationId} UserId={UserId} Role={Role}",
                correlationIdAccessor.CorrelationId, entraUserId, appRoleValue);
            throw;
        }

        logger.LogInformation("Graph app role assigned. CorrelationId={CorrelationId} UserId={UserId} Role={Role}",
            correlationIdAccessor.CorrelationId, entraUserId, appRoleValue);
    }

    private async Task<User?> FindExistingEntraUserAsync(string email, CancellationToken ct)
    {
        var defaultDomain = configuration["Azure:AdOAuth:Domain"];
        var mailNickname = BuildMailNickname(email);
        var userPrincipalName = $"{mailNickname}@{defaultDomain}";
        var guestUpnPrefix = BuildGuestUserPrincipalNamePrefix(email);
        var escapedEmail = EscapeODataString(email);
        var escapedUserPrincipalName = EscapeODataString(userPrincipalName);
        var escapedGuestUpnPrefix = EscapeODataString(guestUpnPrefix);

        logger.LogError("My graph {GraphClient}", graphClient.GetType().FullName);

        try
        {
            var result = await graphClient.Users.GetAsync(r =>
            {
                r.QueryParameters.Top = 1;
            }, ct);
            return result?.Value?.FirstOrDefault();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Graph query failed {DirectoryRoles}, {Me}", graphClient.DirectoryRoles.ToString(), graphClient.Me);
            throw;
        }
    }

    private static string BuildMailNickname(string email) =>
        email.Split('@')[0].Replace(".", "").Replace("-", "");

    private static string BuildGuestUserPrincipalNamePrefix(string email) =>
        email.Replace('@', '_') + "#EXT#";

    private static string EscapeODataString(string value) => value.Replace("'", "''");

    private static string ResolveEntraMail(User user, string fallbackEmail) =>
        !string.IsNullOrWhiteSpace(user.Mail)
            ? user.Mail
            : user.OtherMails?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
              ?? user.UserPrincipalName
              ?? fallbackEmail;

    private async Task<Invitation> CreateExternalInviteAsync(Invitation invitation, string email, CancellationToken ct)
    {
        logger.LogInformation("Graph inviting external user. CorrelationId={CorrelationId} Email={Email}",
            correlationIdAccessor.CorrelationId, email);

        Invitation? createdInvitation;
        try
        {
            createdInvitation = await graphClient.Invitations.PostAsync(invitation, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Graph external invite failed. CorrelationId={CorrelationId} Email={Email}",
                correlationIdAccessor.CorrelationId, email);
            throw;
        }

        if (createdInvitation == null)
        {
            logger.LogError("Graph external invite returned null. CorrelationId={CorrelationId} Email={Email}",
                correlationIdAccessor.CorrelationId, email);
            throw new InvalidOperationException($"User {email} could not be invited");
        }

        logger.LogInformation("Graph external user invited. CorrelationId={CorrelationId} Email={Email} EntraId={EntraId}",
            correlationIdAccessor.CorrelationId, email, createdInvitation.InvitedUser?.Id);

        return createdInvitation;
    }

    private async Task<ServicePrincipal> FetchServicePrincipalAsync(string? appId, CancellationToken ct)
    {
        logger.LogInformation("Graph fetching service principal. CorrelationId={CorrelationId} AppId={AppId}",
            correlationIdAccessor.CorrelationId, appId);

        ServicePrincipalCollectionResponse? servicePrincipals;
        try
        {
            servicePrincipals = await graphClient.ServicePrincipals.GetAsync(request =>
            {
                request.QueryParameters.Filter = $"appId eq '{appId}'";
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Graph fetch service principal failed. CorrelationId={CorrelationId} AppId={AppId}",
                correlationIdAccessor.CorrelationId, appId);
            throw;
        }

        logger.LogInformation("Graph service principal fetched. CorrelationId={CorrelationId}",
            correlationIdAccessor.CorrelationId);

        return servicePrincipals?.Value?.SingleOrDefault()
            ?? throw new InvalidOperationException("API service principal not found.");
    }

    private static bool IsDuplicateAppRoleAssignment(ODataError error) =>
        error.ResponseStatusCode == 400
        && (error.Error?.Code?.Contains("Request_BadRequest", StringComparison.OrdinalIgnoreCase) == true
            || error.Error?.Message?.Contains("Permission being assigned already exists", StringComparison.OrdinalIgnoreCase) == true);

    private static AppRole FindAppRole(ServicePrincipal servicePrincipal, string appRoleValue)
    {
        var appRole = servicePrincipal.AppRoles?.SingleOrDefault(r => r.Value == appRoleValue && r.IsEnabled == true);
        if (appRole?.Id is null)
            throw new InvalidOperationException($"App role '{appRoleValue}' not found.");

        return appRole;
    }
}

public record CreateEntraUserResult(
    string EntraUserId,
    string EntraMail,
    string DisplayName,
    bool Created
);
