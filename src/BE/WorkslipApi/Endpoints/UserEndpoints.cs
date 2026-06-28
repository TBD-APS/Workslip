using Workslip.Api.Helpers;
using Workslip.Api.ViewModels;
using Workslip.Application.Invitations;
using Workslip.Application.Users;

namespace Workslip.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapAdminGroup("/api/users", "users");

        group.MapPost("/", async (CreateUserRequest request, IUserService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, UserViewModelBuilder.ToUser);
        }).Produces<UserViewModel>();

        group.MapGet("/{id}", async (Guid id, IUserService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetDetailAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result, UserViewModelBuilder.ToUserDetail);
        }).Produces<UserDetailViewModel>()
        .RequireAuthorization(AuthPolicies.RequireUser);

        group.MapGet("/", async (int? limit, int? offset, IUserService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetByOrganizationAsync(limit, offset, cancellationToken);
            return ResultExtensions.ToHttpResult(result, UserViewModelBuilder.ToUserList);
        }).Produces<UserListViewModel>();

        group.MapPatch("/{id}", async (Guid id, UpdateUserRequest request, IUserService service, CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(id, request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, UserViewModelBuilder.ToUser);
        }).Produces<UserViewModel>().RequireAuthorization(AuthPolicies.RequireUser); 

        group.MapDelete("/{id}", async (Guid id, IUserService service, CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        return app;
    }
}
