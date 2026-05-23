using Workslip.Application.Auth;
using Workslip.Application.Users;

namespace Workslip.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("auth");

        group.MapPost("/send-code", async (SendCodeRequest request, IAuthService service, CancellationToken cancellationToken) =>
        {
            await service.SendLoginCodeAsync(request, cancellationToken);
            return Results.Ok(new { message = "Hvis e-mailen findes, er en kode sendt." });
        });

        group.MapPost("/verify-code", async (VerifyCodeRequest request, IAuthService service, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            var user = await service.VerifyLoginCodeAsync(request, cancellationToken);
            if (user is null)
                return Results.Unauthorized();

            var token = JwtHelper.GenerateToken(user, configuration);
            return Results.Ok(token);
        });

        group.MapPost("/verify-invite", async (VerifyInviteRequest request, IUserService service, CancellationToken cancellationToken) =>
        {
            var result = await service.VerifyInviteAsync(request, cancellationToken);
            return result.Valid ? Results.Ok(result) : Results.BadRequest(new { error = result.Error });
        });
        return app;
    }
}
