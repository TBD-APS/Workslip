using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Workslip.Domain;

namespace Workslip.Application.Users;

public sealed class UserEntraService(
    ILogger<UserEntraService> logger,
    GraphServiceClient graphClient,
    IConfiguration configuration,
    ICorrelationIdAccessor correlationIdAccessor) : ISuperadminEntraService
{
    public Task<CreateEntraUserResult> CreateUserAsync(string email, string displayName, CancellationToken ct) =>
        EnsureInvitedUserAsync(
            email,
            displayName,
            Roles.User,
            sendInvitationMessage: false,
            redirectPath: "/invite",
            ct);

    public Task<CreateEntraUserResult> InviteAdminAsync(string email, string displayName, CancellationToken ct) =>
        EnsureInvitedUserAsync(
            email,
            displayName,
            Roles.Admin,
            sendInvitationMessage: true,
            redirectPath: "/login",
            ct);

    public Task<CreateEntraUserResult> EnsureSuperadminAsync(
        string email,
        string displayName,
        CancellationToken cancellationToken) =>
        EnsureInvitedUserAsync(
            email,
            displayName,
            Roles.Superadmin,
            sendInvitationMessage: true,
            redirectPath: "/login",
            cancellationToken);

    public Task<CreateEntraUserResult> EnsureInvitedUserAsync(string email, CancellationToken ct) =>
        EnsureInvitedUserAsync(
            email,
            email,
            Roles.User,
            sendInvitationMessage: false,
            redirectPath: "/invite",
            ct);

    private async Task<CreateEntraUserResult> EnsureInvitedUserAsync(
        string email,
        string displayName,
        string appRoleValue,
        bool sendInvitationMessage,
        string redirectPath,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(configuration["Azure:AdOAuth:ClientId"]))
        {
            if (!string.Equals(
                configuration["ASPNETCORE_ENVIRONMENT"],
                "Development",
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Azure:AdOAuth:ClientId is not configured; Entra identities cannot be provisioned outside Development.");
            }

            logger.LogInformation(
                "Entra is not configured; returning deterministic local identity without calling Graph. CorrelationId={CorrelationId}",
                correlationIdAccessor.CorrelationId);

            return new CreateEntraUserResult(
                DeterministicLocalEntraId(email),
                email,
                displayName,
                Created: false);
        }

        var existingUser = await FindExistingEntraUserAsync(email, ct);
        if (existingUser != null)
        {
            await AssignAppRoleTo(existingUser.Id!, appRoleValue, ct);
            return new CreateEntraUserResult(
                existingUser.Id!,
                ResolveEntraMail(existingUser, email),
                existingUser.DisplayName ?? displayName,
                Created: false);
        }

        var baseUrl = configuration["Azure:Domain:BaseUrl"]?.TrimEnd('/') ?? string.Empty;
        var invitation = new Invitation
        {
            InvitedUserEmailAddress = email,
            InviteRedirectUrl = baseUrl + redirectPath,
            SendInvitationMessage = sendInvitationMessage,
            InvitedUserDisplayName = displayName
        };

        var createdInvitation = await CreateExternalInviteAsync(invitation, ct);
        var invitedUser = createdInvitation.InvitedUser
            ?? throw new InvalidOperationException("Graph did not return the invited user.");

        try
        {
            await AssignAppRoleTo(invitedUser.Id!, appRoleValue, ct);
        }
        catch
        {
            await DeleteUserAsync(invitedUser.Id!, ct);
            throw;
        }

        return new CreateEntraUserResult(
            invitedUser.Id!,
            ResolveEntraMail(invitedUser, email),
            displayName,
            Created: true);
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

    public async Task RevokeSuperadminAsync(string entraUserId, CancellationToken cancellationToken)
    {
        var appId = configuration["Azure:AdOAuth:ClientId"];
        var servicePrincipal = await FetchServicePrincipalAsync(appId, cancellationToken);
        var superadminRole = FindAppRole(servicePrincipal, Roles.Superadmin);

        logger.LogWarning(
            "Graph revoking Superadmin app role. CorrelationId={CorrelationId} UserId={UserId}",
            correlationIdAccessor.CorrelationId,
            entraUserId);

        var assignments = await graphClient.Users[entraUserId].AppRoleAssignments.GetAsync(
            request =>
            {
                request.QueryParameters.Filter = $"resourceId eq {servicePrincipal.Id}";
                request.QueryParameters.Select = ["id", "appRoleId", "resourceId"];
            },
            cancellationToken);

        var matchingAssignments = assignments?.Value?
            .Where(assignment =>
                assignment.Id is not null &&
                assignment.AppRoleId == superadminRole.Id)
            .ToArray() ?? [];

        foreach (var assignment in matchingAssignments)
        {
            await graphClient.Users[entraUserId]
                .AppRoleAssignments[assignment.Id!]
                .DeleteAsync(cancellationToken: cancellationToken);
        }

        logger.LogInformation(
            "Graph Superadmin app role revoked. CorrelationId={CorrelationId} UserId={UserId} AssignmentCount={AssignmentCount}",
            correlationIdAccessor.CorrelationId,
            entraUserId,
            matchingAssignments.Length);
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

        try
        {
            var result = await graphClient.Users.GetAsync(
                request =>
                {
                    request.QueryParameters.Filter =
                        $"mail eq '{escapedEmail}' or otherMails/any(m:m eq '{escapedEmail}') or userPrincipalName eq '{escapedUserPrincipalName}' or startswith(userPrincipalName,'{escapedGuestUpnPrefix}')";
                    request.QueryParameters.Select = ["id", "displayName", "userPrincipalName", "mail", "otherMails"];
                    request.QueryParameters.Top = 1;
                }, ct);
            return result?.Value?.FirstOrDefault();
        }
        catch (ODataError odataError)
        {
            logger.LogError(odataError, "Graph API returnerede en fejl: {Code} - {Message}",
                odataError.Error?.Code,
                odataError.Error?.Message);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Generel fejl under kald til Graph API");
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

    private async Task<Invitation> CreateExternalInviteAsync(Invitation invitation, CancellationToken ct)
    {
        logger.LogInformation("Graph inviting external user. CorrelationId={CorrelationId}",
            correlationIdAccessor.CorrelationId);

        Invitation? createdInvitation;
        try
        {
            createdInvitation = await graphClient.Invitations.PostAsync(invitation, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Graph external invite failed. CorrelationId={CorrelationId}",
                correlationIdAccessor.CorrelationId);
            throw;
        }

        if (createdInvitation == null)
        {
            logger.LogError("Graph external invite returned null. CorrelationId={CorrelationId}",
                correlationIdAccessor.CorrelationId);
            throw new InvalidOperationException("External user could not be invited.");
        }

        logger.LogInformation("Graph external user invited. CorrelationId={CorrelationId} EntraId={EntraId}",
            correlationIdAccessor.CorrelationId, createdInvitation.InvitedUser?.Id);

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
        var appRole = servicePrincipal.AppRoles?.SingleOrDefault(role => role.Value == appRoleValue && role.IsEnabled == true);
        if (appRole?.Id is null)
        {
            throw new InvalidOperationException($"App role '{appRoleValue}' not found.");
        }

        return appRole;
    }

    private static string DeterministicLocalEntraId(string email)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()));
        return new Guid(hash.AsSpan(0, 16)).ToString();
    }
}

public record CreateEntraUserResult(
    string EntraUserId,
    string EntraMail,
    string DisplayName,
    bool Created);
