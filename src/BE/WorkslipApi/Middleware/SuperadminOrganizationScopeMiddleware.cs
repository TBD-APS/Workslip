using System.Security.Claims;
using Workslip.Api.Helpers;
using Workslip.Application.Organizations;
using Workslip.Domain;

namespace Workslip.Api.Middleware;

public sealed class SuperadminOrganizationScopeMiddleware(
    RequestDelegate next,
    ILogger<SuperadminOrganizationScopeMiddleware> logger)
{
    public async Task InvokeAsync(
        HttpContext context,
        IOrganizationAdministrationRepository organizations)
    {
        if (!IsSuperadmin(context.User))
        {
            await next(context);
            return;
        }

        var rawScope = context.Request.Headers[CurrentUserContext.OrganizationScopeHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(rawScope))
        {
            await next(context);
            return;
        }

        if (!Guid.TryParse(rawScope, out var organizationId))
        {
            logger.LogWarning(
                "Ignoring malformed Superadmin organization scope. UserId: {UserId}.",
                context.User.FindFirstValue("workslipUserId")
                    ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier));
            await next(context);
            return;
        }

        var organization = await organizations.GetOrganizationAsync(
            organizationId,
            context.RequestAborted);
        if (organization is null)
        {
            logger.LogWarning(
                "Ignoring unknown Superadmin organization scope. UserId: {UserId}. OrganizationId: {OrganizationId}.",
                context.User.FindFirstValue("workslipUserId")
                    ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier),
                organizationId);
            await next(context);
            return;
        }

        context.Items[CurrentUserContext.ValidatedOrganizationScopeItem] = organizationId;
        await next(context);
    }

    private static bool IsSuperadmin(ClaimsPrincipal user) =>
        string.Equals(
            user.FindFirstValue(ClaimTypes.Role)
                ?? user.FindFirstValue("roles")
                ?? user.FindFirstValue("role"),
            Roles.Superadmin,
            StringComparison.OrdinalIgnoreCase);
}
