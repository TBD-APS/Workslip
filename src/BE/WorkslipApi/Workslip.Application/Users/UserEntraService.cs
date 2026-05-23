using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Workslip.Application.Users;

public sealed class UserEntraService(ILogger<UserEntraService> logger, GraphServiceClient _graphClient, IConfiguration _configuration) : IUserEntraService
{
     public async Task<CreateEntraUserResult> CreateUserAsync(string email,string displayName, CancellationToken ct)
    {
        var defaultDomain = _configuration["GraphApp:DefaultUserDomain"];

        var mailNickname = email.Split('@')[0]
            .Replace(".", "")
            .Replace("-", "");

        var userPrincipalName = $"{mailNickname}@{defaultDomain}";
        var tempPassword = $"Tmp-{Guid.NewGuid():N}!aA1";

        var user = await _graphClient.Users.PostAsync(new User
        {
            AccountEnabled = true,
            DisplayName = displayName,
            MailNickname = mailNickname,
            UserPrincipalName = userPrincipalName,
            PasswordProfile = new PasswordProfile
            {
                Password = tempPassword,
                ForceChangePasswordNextSignIn = true
            }
        }, cancellationToken: ct);

        if(user == null)
        {
            logger.LogError("Failed to create user {Email}", email);
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
        var workslipServerAppId = _configuration["GraphApp:WorkslipServerAppId"];
        var servicePrincipals = await _graphClient.ServicePrincipals.GetAsync(request =>
        {
            request.QueryParameters.Filter = $"appId eq '{workslipServerAppId}'";
        }, ct);

        var apiServicePrincipal = servicePrincipals?.Value?.SingleOrDefault()
            ?? throw new InvalidOperationException("API service principal not found.");

        var appRole = apiServicePrincipal.AppRoles?
            .SingleOrDefault(r => r.Value == appRoleValue && r.IsEnabled == true);

        if (appRole?.Id is null)
            throw new InvalidOperationException($"App role '{appRoleValue}' not found.");

        await _graphClient.Users[entraUserId].AppRoleAssignments.PostAsync(
            new AppRoleAssignment
            {
                PrincipalId = Guid.Parse(entraUserId),
                ResourceId = Guid.Parse(apiServicePrincipal.Id!),
                AppRoleId = appRole.Id.Value
            },
            cancellationToken: ct
        );
    }
}

public record CreateEntraUserResult(
    string EntraUserId,
    string EntraMail,
    string DisplayName
);
