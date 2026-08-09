using Workslip.Application.Auth;
using Workslip.Application.Users;

namespace Workslip.Api.Endpoints;

public static class DevEndpoints
{
    public static WebApplication MapDevEndpoints(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
            return app;

        var group = app.MapGroup("/api/dev").WithTags("dev");

        group.MapPost("/token", async (
            DevTokenRequest request,
            IUserRepository users,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            var user = await users.GetByEmailAsync(request.Email, cancellationToken);
            if (user is null)
                return Results.NotFound(new { error = "User not found" });

            var authUser = new AuthUserInfo(
                user.Id,
                user.OrganizationId,
                user.Email,
                user.DisplayName,
                user.Role);

            return Results.Ok(JwtHelper.GenerateToken(authUser, configuration));
        })
        .Produces<AuthTokenResponse>()
        .AllowAnonymous();

        return app;
    }
}

public sealed record DevTokenRequest(string Email);
