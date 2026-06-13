using Workslip.Api.Helpers;
using Workslip.Api.ViewModels;
using Workslip.Application.Auth;
using Workslip.Application.Invitations;
using Workslip.Application.Users;

namespace Workslip.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("auth");

        group.MapGet("/me", async (IAuthService service, CancellationToken cancellationToken) =>
        {
            var me = await service.GetCurrentUserAsync(cancellationToken);
            return Results.Ok(UserViewModelBuilder.ToUser(me));
        }).Produces<UserViewModel>().RequireAuthorization(AuthPolicies.RequireUser);

        group.MapPatch("/me", async (UpdateUserRequest request, IAuthService service, CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateCurrentUserAsync(request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, UserViewModelBuilder.ToUser);
        }).Produces<UserViewModel>().RequireAuthorization(AuthPolicies.RequireUser);

        group.MapPost("/send-code", async (SendCodeRequest request, IAuthService service, CancellationToken cancellationToken) =>
        {
            await service.SendLoginCodeAsync(request, cancellationToken);
            return Results.Ok(new { message = "Hvis e-mailen findes, er en kode sendt." });
        });

        group.MapPost("/verify-code/{code}", async (string code, SendCodeRequest request, IAuthService service, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            var result = await service.VerifyLoginCodeAsync(new VerifyCodeRequest(request.Email, code), cancellationToken);
            return ResultExtensions.ToHttpResult(result, user => JwtHelper.GenerateToken(user, configuration));
        }).Produces<AuthTokenResponse>();

        group.MapPost("/verify-invite/{token}", async (string token, CompleteInviteRequest body, IInvitationService service, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            var result = await service.VerifyInviteAsync(new VerifyInviteRequest(token, body.DisplayName, body.Phone), cancellationToken);
            return ResultExtensions.ToHttpResult(result, user => JwtHelper.GenerateToken(user, configuration));
        }).Produces<AuthTokenResponse>();

        group.MapPost("/invite", async (InviteUsersRequest request, IInvitationService service, CancellationToken cancellationToken) =>
        {
            var result = await service.InviteUsersAsync(request, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        }).RequireAuthorization(AuthPolicies.RequireAdmin);

        group.MapGet("/invites", async (IInvitationService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetOrganizationInvitesAsync(cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        }).RequireAuthorization(AuthPolicies.RequireAdmin);

        group.MapPost("/invite/{token}/open", async (string token, IInvitationService service, CancellationToken cancellationToken) =>
        {
            await service.MarkOpenedAsync(token, cancellationToken);
            return Results.Ok();
        });

        return app;
    }
}
