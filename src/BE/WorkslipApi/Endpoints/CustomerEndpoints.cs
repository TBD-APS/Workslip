using Workslip.Api.Helpers;
using Workslip.Api.ViewModels;
using Workslip.Application.Auth;
using Workslip.Application.Customers;

namespace Workslip.Api.Endpoints;

public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var searchGroup = app.MapGroup("/api/customers")
            .WithTags("customers")
            .RequireAuthorization(AuthPolicies.RequireUser);

        searchGroup.MapGet("/search", async (string? query, int? limit, ICustomerService service, CancellationToken cancellationToken) =>
        {
            var result = await service.SearchAsync(query, limit, cancellationToken);
            return ResultExtensions.ToHttpResult(result, customers => customers.Select(CustomerViewModelBuilder.ToSearch).ToArray());
        }).Produces<List<CustomerSearchViewModel>>();

        searchGroup.MapGet("/suggest", async (string? query, int? limit, ICustomerService service, CancellationToken cancellationToken) =>
        {
            var result = await service.SearchAsync(query, limit, cancellationToken);
            return ResultExtensions.ToHttpResult(result, customers => customers.Select(CustomerViewModelBuilder.ToSearch).ToArray());
        }).Produces<List<CustomerSearchViewModel>>();

        searchGroup.MapGet("/top", async (int? limit, ICustomerService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetTopAsync(limit ?? 3, cancellationToken);
            return ResultExtensions.ToHttpResult(result, customers => customers.Select(CustomerViewModelBuilder.ToSearch).ToArray());
        }).Produces<List<CustomerSearchViewModel>>();

        var group = app.MapGroup("/api/customers")
            .WithTags("customers")
            .RequireAuthorization(AuthPolicies.RequireAdmin);

        group.MapGet("/", async (int? limit, int? offset, ICustomerService service, CancellationToken cancellationToken) =>
        {
            var result = await service.ListAsync(limit, offset, cancellationToken);
            return ResultExtensions.ToHttpResult(result, customers => customers.Select(CustomerViewModelBuilder.ToListItem).ToArray());
        }).Produces<List<CustomerListItemViewModel>>();

        group.MapGet("/{id:guid}", async (Guid id, ICustomerService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetByIdAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result, customer => CustomerViewModelBuilder.ToDetail(customer));
        }).Produces<CustomerDetailViewModel>();

        group.MapPut("/{id:guid}", async (Guid id, UpdateCustomerRequest request, ICustomerService service, CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(id, request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, customer => CustomerViewModelBuilder.ToDetail(customer));
        }).Produces<CustomerDetailViewModel>();

        group.MapDelete("/{id:guid}", async (Guid id, ICustomerService service, CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        }).Produces<Result>();

        return app;
    }
}
