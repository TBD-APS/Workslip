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

        group.MapPost("/", async (CreateUserRequest request, IUserService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.CreateAsync(request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, UserViewModelBuilder.ToUser);
        }).Produces<UserViewModel>();

        group.MapGet("/{id}", async (Guid id, IUserService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.GetDetailAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result, UserViewModelBuilder.ToUserDetail);
        }).Produces<UserDetailViewModel>()
        .RequireAuthorization(AuthPolicies.RequireUser);

        group.MapGet("/{id}/billing-rate", async (Guid id, IUserBillingService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            return ResultExtensions.ToHttpResult(await service.GetAsync(id, cancellationToken));
        }).Produces<UserBillingRateResponse>();

        group.MapPatch("/{id}/billing-rate", async (Guid id, UpdateBillableHourlyRateRequest request, IUserBillingService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            return ResultExtensions.ToHttpResult(await service.UpdateAsync(id, request, cancellationToken));
        }).Produces(StatusCodes.Status204NoContent).ProducesValidationProblem();

        group.MapGet("/", async ([AsParameters] ListQueryOptions query, IUserService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.GetByOrganizationAsync(
                query.Limit,
                query.Offset,
                query.Search,
                query.SortBy,
                query.SortDirection,
                cancellationToken);
            return ResultExtensions.ToHttpResult(result, UserViewModelBuilder.ToUserList);
        }).Produces<UserListViewModel>();

        group.MapPatch("/{id}", async (Guid id, UpdateUserRequest request, IUserService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.UpdateAsync(id, request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, UserViewModelBuilder.ToUser);
        }).Produces<UserViewModel>().RequireAuthorization(AuthPolicies.RequireUser);

        group.MapDelete("/{id}", async (Guid id, IUserService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.DeleteAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        return app;
    }
}
