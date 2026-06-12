using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Workslip.Application.Users;

public sealed class UserEntraService(
    ILogger<UserEntraService> logger,
    GraphServiceClient graphClient,
    IConfiguration configuration,
    ICorrelationIdAccessor correlationIdAccessor) : IUserEntraService
{
     public async Task<CreateEntraUserResult> CreateUserAsync(string email, string displayName, CancellationToken ct)
    {
        var defaultDomain = configuration["Azure:AdOAuth:Domain"];
        var mailNickname = BuildMailNickname(email);
        var userPrincipalName = $"{mailNickname}@{defaultDomain}";

        var existingUser = await FindExistingEntraUserAsync(mailNickname, userPrincipalName, ct);
        if (existingUser != null)
        {
            return new CreateEntraUserResult(existingUser.Id!, existingUser.UserPrincipalName!, displayName, Created: false);
        }

        var newUser = BuildEntraUser(displayName, mailNickname, userPrincipalName);
        var createdUser = await CreateEntraUserAsync(newUser, displayName, email, userPrincipalName, ct);

        try
        {
            await AssignAppRoleTo(createdUser.Id!, "User", ct);
        }
        catch
        {
            await DeleteUserAsync(createdUser.Id!, ct);
            throw;
        }

        return new CreateEntraUserResult(createdUser.Id!, userPrincipalName, displayName, Created: true);
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
        catch (Exception ex)
        {
            logger.LogError(ex, "Graph assign app role failed. CorrelationId={CorrelationId} UserId={UserId} Role={Role}",
                correlationIdAccessor.CorrelationId, entraUserId, appRoleValue);
            throw;
        }

        logger.LogInformation("Graph app role assigned. CorrelationId={CorrelationId} UserId={UserId} Role={Role}",
            correlationIdAccessor.CorrelationId, entraUserId, appRoleValue);
    }

    private static string BuildMailNickname(string email) =>
        email.Split('@')[0].Replace(".", "").Replace("-", "");

    private async Task<User?> FindExistingEntraUserAsync(string mailNickname, string userPrincipalName, CancellationToken ct)
    {
        var result = await graphClient.Users.GetAsync(
            request =>
            {
                request.QueryParameters.Filter = $"mail eq '{mailNickname}' or userPrincipalName eq '{userPrincipalName}'";
                request.QueryParameters.Select = ["id", "displayName", "userPrincipalName", "mail"];
                request.QueryParameters.Top = 1;
            }, ct);

        return result?.Value?.FirstOrDefault();
    }

    private static User BuildEntraUser(string displayName, string mailNickname, string userPrincipalName) =>
        new()
        {
            AccountEnabled = true,
            DisplayName = displayName,
            MailNickname = mailNickname,
            UserPrincipalName = userPrincipalName,
            PasswordProfile = new PasswordProfile
            {
                Password = $"Tmp-{Guid.NewGuid():N}!aA1",
                ForceChangePasswordNextSignIn = true
            }
        };

    private async Task<User> CreateEntraUserAsync(User newUser, string displayName, string email, string userPrincipalName, CancellationToken ct)
    {
        logger.LogInformation("Graph creating user. CorrelationId={CorrelationId} Email={Email} Upn={Upn}",
            correlationIdAccessor.CorrelationId, email, userPrincipalName);

        User? user;
        try
        {
            user = await graphClient.Users.PostAsync(newUser, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Graph create user failed. CorrelationId={CorrelationId} Email={Email}",
                correlationIdAccessor.CorrelationId, email);
            throw;
        }

        if (user == null)
        {
            logger.LogError("Graph create user returned null. CorrelationId={CorrelationId} Email={Email}",
                correlationIdAccessor.CorrelationId, email);
            throw new InvalidOperationException($"User {displayName} could not be created");
        }

        logger.LogInformation("Graph user created. CorrelationId={CorrelationId} Email={Email} EntraId={EntraId}",
            correlationIdAccessor.CorrelationId, email, user.Id);

        return user;
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
