using Workslip.Api.Helpers;
using Workslip.Application.Auth;
using Workslip.Application.Users;

namespace Workslip.Api.Endpoints;

public static class DevEndpoints
{
    public static IEndpointRouteBuilder MapDevEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dev").WithTags("dev").AllowAnonymous();

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
        });

        return app;
    }
}

public sealed record DevTokenRequest(string Email);
