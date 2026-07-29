using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using Workslip.Api.Helpers;
using Workslip.Application.Organizations;
using Workslip.Domain;

namespace Workslip.Api.Middleware;

public sealed class SuperadminOrganizationScopeMiddleware(
    RequestDelegate next,
    ILogger<SuperadminOrganizationScopeMiddleware> logger)
{
    private static readonly TimeSpan ExistingOrganizationCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MissingOrganizationCacheDuration = TimeSpan.FromSeconds(30);

    public async Task InvokeAsync(
        HttpContext context,
        IOrganizationAdministrationRepository organizations,
        IMemoryCache cache)
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

        var cacheKey = $"auth:superadmin-organization:{organizationId:N}";
        if (!cache.TryGetValue(cacheKey, out bool organizationExists))
        {
            organizationExists = await organizations.GetOrganizationAsync(
                organizationId,
                context.RequestAborted) is not null;
            cache.Set(
                cacheKey,
                organizationExists,
                organizationExists
                    ? ExistingOrganizationCacheDuration
                    : MissingOrganizationCacheDuration);
        }

        if (!organizationExists)
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
