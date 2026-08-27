using Workslip.Api.Helpers;
using Workslip.Application.Integrations;

namespace Workslip.Api.Endpoints;

public static class AccountingProviderSettingsEndpoints
{
    public static IEndpointRouteBuilder MapAccountingProviderSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapAdminGroup("/api/settings/accounting", "accounting-settings");

        group.MapGet("/", async (
            IAccountingProviderSettingsService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.GetAsync(cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        })
        .Produces<AccountingProviderSettingsResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound);

        group.MapPut("/", async (
            UpdateAccountingProviderRequest request,
            IAccountingProviderSettingsService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.UpdateAsync(request, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        })
        .Produces(StatusCodes.Status204NoContent)
        .ProducesValidationProblem()
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
