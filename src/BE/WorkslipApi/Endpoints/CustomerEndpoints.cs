using Workslip.Api.Helpers;
using Workslip.Api.ViewModels;
using Workslip.Application.Auth;
using Workslip.Application.Customers;

namespace Workslip.Api.Endpoints;

public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
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

        return app;
    }
}
