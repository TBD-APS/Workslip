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
        var defaultDomain = configuration["Azure:GraphApp:DefaultUserDomain"];

        var mailNickname = email.Split('@')[0].Replace(".", "").Replace("-", "");

        var userPrincipalName = $"{mailNickname}@{defaultDomain}";

        logger.LogInformation("Graph creating user. CorrelationId={CorrelationId} Email={Email} Upn={Upn}",correlationIdAccessor.CorrelationId, email, userPrincipalName);

        User? user;
        try
        {
            user = await graphClient.Users.PostAsync(new User
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
            }, cancellationToken: ct);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Graph create user failed. CorrelationId={CorrelationId} Email={Email}", correlationIdAccessor.CorrelationId, email);
            throw;
        }

        logger.LogInformation("Graph user created. CorrelationId={CorrelationId} Email={Email} EntraId={EntraId}", correlationIdAccessor.CorrelationId, email, user?.Id);

        if (user == null)
        {
            logger.LogError("Graph create user returned null. CorrelationId={CorrelationId} Email={Email}", correlationIdAccessor.CorrelationId, email);
            throw new InvalidOperationException($"User {displayName} could not be created");
        }

        return new CreateEntraUserResult(
            EntraUserId: user.Id!,
            EntraMail: userPrincipalName,
            DisplayName: displayName
        );
    }

    public async Task AssignAppRoleTo(string entraUserId, string appRoleValue, CancellationToken ct)
    {
        var workslipServerAppId = configuration["Azure:GraphApp:WorkslipServerAppId"];

        logger.LogInformation("Graph fetching service principal. CorrelationId={CorrelationId} AppId={AppId}", correlationIdAccessor.CorrelationId, workslipServerAppId);

        var startTime = DateTimeOffset.UtcNow;
        ServicePrincipalCollectionResponse? servicePrincipals;
        try
        {
            servicePrincipals = await graphClient.ServicePrincipals.GetAsync(request =>
            {
                request.QueryParameters.Filter = $"appId eq '{workslipServerAppId}'";
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Graph fetch service principal failed. CorrelationId={CorrelationId} AppId={AppId}", correlationIdAccessor.CorrelationId, workslipServerAppId);
            throw;
        }

        logger.LogInformation("Graph service principal fetched. CorrelationId={CorrelationId}", correlationIdAccessor.CorrelationId);

        var apiServicePrincipal = servicePrincipals?.Value?.SingleOrDefault()?? throw new InvalidOperationException("API service principal not found.");

        var appRole = apiServicePrincipal.AppRoles?.SingleOrDefault(r => r.Value == appRoleValue && r.IsEnabled == true);

        if (appRole?.Id is null)
            throw new InvalidOperationException($"App role '{appRoleValue}' not found.");

        logger.LogInformation("Graph assigning app role. CorrelationId={CorrelationId} UserId={UserId} Role={Role}", correlationIdAccessor.CorrelationId, entraUserId, appRoleValue);

        startTime = DateTimeOffset.UtcNow;
        try
        {
            await graphClient.Users[entraUserId].AppRoleAssignments.PostAsync(
                new AppRoleAssignment
                {
                    PrincipalId = Guid.Parse(entraUserId),
                    ResourceId = Guid.Parse(apiServicePrincipal.Id!),
                    AppRoleId = appRole.Id.Value
                },
                cancellationToken: ct
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Graph assign app role failed. CorrelationId={CorrelationId} UserId={UserId} Role={Role}", correlationIdAccessor.CorrelationId, entraUserId, appRoleValue);
            throw;
        }

        logger.LogInformation("Graph app role assigned. CorrelationId={CorrelationId} UserId={UserId} Role={Role}", correlationIdAccessor.CorrelationId, entraUserId, appRoleValue);
    }
}

public record CreateEntraUserResult(
    string EntraUserId,
    string EntraMail,
    string DisplayName
);
