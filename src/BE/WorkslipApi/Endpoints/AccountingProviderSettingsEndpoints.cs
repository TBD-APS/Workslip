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
        });

        group.MapPut("/", async (
            UpdateAccountingProviderRequest request,
            IAccountingProviderSettingsService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.UpdateAsync(request, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        return app;
    }
}
