using Workslip.Api.Helpers;
using Workslip.Api.ViewModels;
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
            return ResultExtensions.ToHttpResult(result, UserViewModelBuilder.ToUser);
        });

        group.MapGet("/{id}", async (Guid id, IUserService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result, UserViewModelBuilder.ToUser);
        }).RequireAuthorization(AuthPolicies.RequireUser);

        group.MapGet("/", async (IUserService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetByOrganizationAsync(cancellationToken);
            return ResultExtensions.ToHttpResult(result, UserViewModelBuilder.ToUserList);
        });

        group.MapPatch("/{id}", async (Guid id, UpdateUserRequest request, IUserService service, CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(id, request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, UserViewModelBuilder.ToUser);
        }).RequireAuthorization(AuthPolicies.RequireUser); 

        group.MapDelete("/{id}", async (Guid id, IUserService service, CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        return app;
    }
}
