using Workslip.Api.Helpers;
using Workslip.Application.Auth;
using Workslip.Application.Users;

namespace Workslip.Api.Endpoints;

public static class DevEndpoints
{
    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dev").WithTags("dev");

        group.MapPost("/token", async (DevTokenRequest request, IUserRepository users, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            var user = await users.GetByEmailAsync(request.Email, cancellationToken);
            if (user is null)
            {
                return Results.NotFound(new { error = "User not found" });
            }

            var authUser = new AuthUserInfo(
                user.Id,
                user.OrganizationId,
                user.Email,
                user.DisplayName,
                user.Role);

            var response = JwtHelper.GenerateToken(authUser, configuration);
            return Results.Ok(response);
        }).Produces<AuthTokenResponse?>().AllowAnonymous();


        group.MapGet("/debug", (HttpContext httpContext, ICurrentUserContext currentUser) =>
        {
            var test = new
            {
                IsAuthenticated = httpContext.User.Identity?.IsAuthenticated,
                AuthenticationType = httpContext.User.Identity?.AuthenticationType,
                CurrentUserId = currentUser.UserId,
                CurrentOrganizationId = currentUser.OrganizationId,
                CurrentRole = currentUser.Role,
                Claims = httpContext.User.Claims.Select(c => new
                {
                    c.Type,
                    c.Value
                })
            };

        return Results.Ok(test);
    }).RequireAuthorization();
        
        return app;
    }


}

public sealed record DevTokenRequest(string Email);
