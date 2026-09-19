using Workslip.Application.Auth;
using Workslip.Application.Users;
using Workslip.Api.Helpers;

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
            [Microsoft.AspNetCore.Mvc.FromServices] IRefreshSessionService sessions,
            IConfiguration configuration,
            HttpContext context,
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

            await sessions.RevokeAsync(RefreshSessionCookie.Read(context.Request), cancellationToken);
            var grant = await sessions.StartAsync(authUser, cancellationToken);
            RefreshSessionCookie.Write(context, grant.RefreshToken, grant.ExpiresAt);
            return Results.Ok(JwtHelper.GenerateToken(authUser, configuration));
        })
        .Produces<AuthTokenResponse>()
        .AllowAnonymous();

        return app;
    }
}

public sealed record DevTokenRequest(string Email);
