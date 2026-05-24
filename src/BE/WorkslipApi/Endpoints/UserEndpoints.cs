using Workslip.Api.Helpers;
using Workslip.Application.Invitations;
using Workslip.Application.Users;

namespace Workslip.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("users")
            .RequireAuthorization(AuthPolicies.RequireAdmin);

        group.MapPost("/", async (CreateUserRequest request, IUserService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        group.MapGet("/{id}", async (Guid id, IUserService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        group.MapGet("/organization/{organizationId}", async (Guid organizationId, IUserService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetByOrganizationAsync(organizationId, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        group.MapPatch("/{id}", async (Guid id, UpdateUserRequest request, IUserService service, CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(id, request, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        group.MapDelete("/{id}", async (Guid id, IUserService service, CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        group.MapPost("/invite", async (InviteUsersRequest request, IInvitationService service, CancellationToken cancellationToken) =>
        {
            var result = await service.InviteUsersAsync(request, cancellationToken);
            return Results.Ok(result);
        });

        return app;
    }
}
