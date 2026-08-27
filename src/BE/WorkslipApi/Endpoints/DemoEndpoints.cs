using Workslip.Api.Configuration;
using Workslip.Application.Auth;
using Workslip.Application.Users;

namespace Workslip.Api.Endpoints;

public static class DemoEndpoints
{
    private const string DefaultDemoAdminEmail = "admin@17v3ygzs.mailosaur.net";

    public static WebApplication MapDemoEndpoints(this WebApplication app)
    {
        if (!DemoModeConfiguration.IsEnabled(app.Environment, app.Configuration))
            return app;

        var group = app.MapGroup("/api/demo").WithTags("demo");

        group.MapPost("/token", async (
            IUserRepository users,
            IConfiguration configuration,
            CancellationToken cancellationToken) =>
        {
            var email = configuration["DemoMode:AdminEmail"]?.Trim();
            if (string.IsNullOrWhiteSpace(email))
                email = DefaultDemoAdminEmail;

            var user = await users.GetByEmailAsync(email, cancellationToken);
            if (user is null)
                return Results.Problem("Demo administrator is not seeded.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var authUser = new AuthUserInfo(
                user.Id,
                user.OrganizationId,
                user.Email,
                user.DisplayName,
                user.Role);

            return Results.Ok(JwtHelper.GenerateToken(authUser, configuration));
        })
        .Produces<AuthTokenResponse>()
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
        .AllowAnonymous();

        return app;
    }
}
