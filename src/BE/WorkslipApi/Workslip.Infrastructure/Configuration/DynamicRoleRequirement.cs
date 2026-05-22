using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;

public class DynamicRoleRequirement : IAuthorizationRequirement
{
    public string RequiredRole { get; }
    public DynamicRoleRequirement(string requiredRole) => RequiredRole = requiredRole;
}

public class DynamicRoleHandler(IConfiguration configuration) : AuthorizationHandler<DynamicRoleRequirement>
{
    private readonly IConfiguration configuration = configuration;

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, DynamicRoleRequirement requirement)
    {
        var userRoles = context.User.Claims
            .Where(c => c.Type == ClaimTypes.Role || c.Type == "roles")
            .Select(c => c.Value)
            .ToList();

        // 2. Tjek om brugeren har den specifikke rolle direkte, eller om de har en overordnet rolle
        foreach (var userRole in userRoles)
        {
            if (userRole == requirement.RequiredRole || IsRoleHigherInHierarchy(userRole, requirement.RequiredRole))
            {
                context.Succeed(requirement);
                break;
            }
        }

        return Task.CompletedTask;
    }

    private bool IsRoleHigherInHierarchy(string currentRole, string targetRole)
    {
        var inheritedRoles = configuration.GetSection($"Authorization:RoleHierarchy:{currentRole}").Get<string[]>();

        if (inheritedRoles == null) return false;

        foreach (var inheritedRole in inheritedRoles)
        {
            if (inheritedRole == targetRole || IsRoleHigherInHierarchy(inheritedRole, targetRole))
            {
                return true;
            }
        }

        return false;
    }
}