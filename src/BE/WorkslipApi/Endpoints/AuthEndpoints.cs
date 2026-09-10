using Workslip.Api.Helpers;
using Workslip.Api.ViewModels;
using Workslip.Application.Auth;
using Workslip.Application.Invitations;
using Workslip.Application.Users;
using ArdalisResult = Ardalis.Result;

namespace Workslip.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("auth");

        group.MapGet("/me", async (IAuthService service, CancellationToken cancellationToken) =>
        {
            try
            {
                var me = await service.GetCurrentUserAsync(cancellationToken);
                return Results.Ok(UserViewModelBuilder.ToUser(me));
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Unauthorized();
            }
        })
        .Produces<UserViewModel>()
        .Produces(StatusCodes.Status401Unauthorized)
        .RequireAuthorization(AuthPolicies.RequireReadAccess);

        group.MapPatch("/me", async (UpdateUserRequest request, IAuthService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.UpdateCurrentUserAsync(request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, UserViewModelBuilder.ToUser);
        }).Produces<UserViewModel>().RequireAuthorization(AuthPolicies.RequireUser);

        group.MapPost("/send-code", async (SendCodeRequest request, IAuthService service, CancellationToken cancellationToken) =>
        {
            await service.SendLoginCodeAsync(request, cancellationToken);
            return Results.Ok(new { message = "Hvis e-mailen findes, er en kode sendt." });
        });

        group.MapPost("/verify-code/{code}", async (string code, SendCodeRequest request, IAuthService service, IRefreshSessionService sessions, IConfiguration configuration, HttpContext context, CancellationToken cancellationToken) =>
        {
            var result = await service.VerifyLoginCodeAsync(new VerifyCodeRequest(request.Email, code), cancellationToken);
            return await CompleteLoginAsync(result, sessions, configuration, context, cancellationToken);
        }).Produces<AuthTokenResponse>();

        group.MapPost("/entra-enroll", async (EntraEnrollRequest request, IInvitationService service, IRefreshSessionService sessions, IConfiguration configuration, HttpContext context, CancellationToken cancellationToken) =>
        {
            var result = await service.CompleteEnrollmentAsync(request, cancellationToken);
            return await CompleteLoginAsync(result, sessions, configuration, context, cancellationToken);
        })
        .Produces<AuthTokenResponse>()
        .RequireAuthorization(policy => policy.AddAuthenticationSchemes("EntraJwt").RequireAuthenticatedUser());

        group.MapPost("/entra-login", async (IAuthService service, IRefreshSessionService sessions, IConfiguration configuration, HttpContext context, CancellationToken cancellationToken) =>
        {
            var result = await service.CompleteEntraLoginAsync(cancellationToken);
            return await CompleteLoginAsync(result, sessions, configuration, context, cancellationToken);
        })
        .Produces<AuthTokenResponse>()
        .RequireAuthorization(policy => policy.AddAuthenticationSchemes("EntraJwt").RequireAuthenticatedUser());

        group.MapPost("/refresh", async (IRefreshSessionService sessions, IConfiguration configuration, HttpContext context, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(context);
            if (!RefreshSessionCookie.HasTrustedOrigin(context.Request, configuration))
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Untrusted request origin");
            }

            var result = await sessions.RefreshAsync(RefreshSessionCookie.Read(context.Request), cancellationToken);
            if (result.IsSuccess)
            {
                RefreshSessionCookie.Write(context, result.Value.RefreshToken, result.Value.ExpiresAt);
                return Results.Ok(JwtHelper.GenerateToken(result.Value.User, configuration));
            }

            if (result.Status == ArdalisResult.ResultStatus.Conflict)
            {
                return Results.Conflict(new
                {
                    error = "refresh_race",
                    message = "En anden forespørgsel fornyede sessionen. Prøv igen."
                });
            }

            RefreshSessionCookie.Clear(context);
            return Results.Unauthorized();
        })
        .Produces<AuthTokenResponse>()
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status409Conflict)
        .RequireRateLimiting("auth-session");

        group.MapPost("/logout", async (IRefreshSessionService sessions, IConfiguration configuration, HttpContext context, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(context);
            if (!RefreshSessionCookie.HasTrustedOrigin(context.Request, configuration))
            {
                return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Untrusted request origin");
            }

            await sessions.RevokeAsync(RefreshSessionCookie.Read(context.Request), cancellationToken);
            RefreshSessionCookie.Clear(context);
            return Results.NoContent();
        })
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status403Forbidden)
        .RequireRateLimiting("auth-session");

        group.MapGet("/invites", async (IInvitationService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetOrganizationInvitesAsync(cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        }).RequireAuthorization(AuthPolicies.RequireAdmin);

        group.MapDelete("/invites/{inviteId:guid}", async (Guid inviteId, IInvitationStatusService service, CancellationToken cancellationToken) =>
        {
            var result = await service.ClearAsync(inviteId, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        }).RequireAuthorization(AuthPolicies.RequireAdmin);

        group.MapPost("/invite", async (InviteUsersRequest request, IInvitationService service, CancellationToken cancellationToken) =>
        {
            var result = await service.InviteUsersAsync(request, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        })
        .Produces<InviteUsersResponse>()
        .RequireAuthorization(AuthPolicies.RequireAdmin);

        group.MapPost("/invite/{token}/open", async (string token, IInvitationService service, CancellationToken cancellationToken) =>
        {
            var result = await service.MarkOpenedAsync(token, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        return app;
    }

    private static async Task<Microsoft.AspNetCore.Http.IResult> CompleteLoginAsync(
        ArdalisResult.Result<AuthUserInfo> login,
        IRefreshSessionService sessions,
        IConfiguration configuration,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        HttpCacheHeaders.SetNoStore(context);
        if (!login.IsSuccess)
        {
            return ResultExtensions.ToHttpResult(login, user => JwtHelper.GenerateToken(user, configuration));
        }

        await sessions.RevokeAsync(RefreshSessionCookie.Read(context.Request), cancellationToken);
        var grant = await sessions.StartAsync(login.Value, cancellationToken);
        RefreshSessionCookie.Write(context, grant.RefreshToken, grant.ExpiresAt);
        return Results.Ok(JwtHelper.GenerateToken(grant.User, configuration));
    }
}
