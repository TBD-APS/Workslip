using Workslip.Api.Helpers;
using Workslip.Application.Users;

namespace Workslip.Api.Endpoints;

public static class JobCostingEndpoints
{
    public static IEndpointRouteBuilder MapJobCostingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapAdminGroup("/api/job-costing", "job-costing");

        group.MapGet("/users/{id}/rate", async (
            Guid id,
            IUserBillingService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.GetAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result, value => value);
        }).Produces<UserBillingRateResponse>();

        group.MapPatch("/users/{id}/rate", async (
            Guid id,
            UpdateBillableHourlyRateRequest request,
            IUserBillingService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.UpdateAsync(id, request, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        return app;
    }
}
