using Workslip.Api.Helpers;
using Workslip.Api.ViewModels;
using Workslip.Application.Users;

namespace Workslip.Api.Endpoints;

public static class SuperadminUserEndpoints
{
    public static IEndpointRouteBuilder MapSuperadminUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapSuperAdminGroup("/api/superadmin/users", "superadmin-users");

        group.MapGet("/", async (
            [AsParameters] AdminUserListQueryOptions query,
            ISuperadminUserService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.ListAsync(
                query.OrganizationId,
                query.Limit,
                query.Offset,
                query.Search,
                query.SortBy,
                query.SortDirection,
                cancellationToken);
            return ResultExtensions.ToHttpResult(result, AdminUserViewModelBuilder.ToAdminUserList);
        }).Produces<AdminUserListViewModel>();

        group.MapGet("/{id:guid}", async (
            Guid id,
            ISuperadminUserService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.GetAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result, AdminUserViewModelBuilder.ToAdminUser);
        }).Produces<AdminUserViewModel>();

        group.MapPost("/", async (
            CreateAdminUserRequest request,
            ISuperadminUserService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.CreateAsync(request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, AdminUserViewModelBuilder.ToAdminUser);
        }).Produces<AdminUserViewModel>();

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateAdminUserRequest request,
            ISuperadminUserService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.UpdateAsync(id, request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, AdminUserViewModelBuilder.ToAdminUser);
        }).Produces<AdminUserViewModel>();

        group.MapDelete("/{id:guid}", async (
            Guid id,
            ISuperadminUserService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.DeleteAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        return app;
    }
}
