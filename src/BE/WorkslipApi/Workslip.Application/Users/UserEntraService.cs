using Microsoft.Extensions.Configuration;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Workslip.Application.Users;

public sealed class UserEntraService(GraphServiceClient _graphClient, IConfiguration _configuration) : IUserEntraService
{
     public async Task<CreateEntraUserResult> CreateUserAsync(
        string email,
        string displayName,
        string appRoleValue,
        CancellationToken ct)
    {
        var mailNickname = email.Split('@')[0]
            .Replace(".", "")
            .Replace("-", "");

        var tempPassword = $"Tmp-{Guid.NewGuid():N}!aA1";

        var user = await _graphClient.Users.PostAsync(new User
        {
            AccountEnabled = true,
            DisplayName = displayName,
            MailNickname = mailNickname,
            UserPrincipalName = email,
            PasswordProfile = new PasswordProfile
            {
                Password = tempPassword,
                ForceChangePasswordNextSignIn = true
            }
        }, cancellationToken: ct);

        if(user == null)
            throw new InvalidOperationException($"User {displayName} could not be created with role {appRoleValue}");

        var apiAppClientId = _configuration["Graph:ApiAppClientId"];

        var servicePrincipals = await _graphClient.ServicePrincipals.GetAsync(request =>
        {
            request.QueryParameters.Filter = $"appId eq '{apiAppClientId}'";
        }, ct);

        var apiServicePrincipal = servicePrincipals?.Value?.SingleOrDefault()
            ?? throw new InvalidOperationException("API service principal not found.");

        var appRole = apiServicePrincipal.AppRoles?
            .SingleOrDefault(r => r.Value == appRoleValue && r.IsEnabled == true);

        if (appRole?.Id is null)
            throw new InvalidOperationException($"App role '{appRoleValue}' not found.");

        await _graphClient.Users[user.Id].AppRoleAssignments.PostAsync(
            new AppRoleAssignment
            {
                PrincipalId = Guid.Parse(user.Id),
                ResourceId = Guid.Parse(apiServicePrincipal.Id!),
                AppRoleId = appRole.Id.Value
            },
            cancellationToken: ct
        );

        return new CreateEntraUserResult(
            AzureObjectId: user.Id!,
            Email: email,
            DisplayName: displayName
        );
    }
}

public record CreateEntraUserResult(
    string AzureObjectId,
    string Email,
    string DisplayName
);
