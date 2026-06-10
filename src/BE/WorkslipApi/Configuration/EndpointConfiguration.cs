using Scalar.AspNetCore;
using Workslip.Api.Endpoints;
using Workslip.Infrastructure;
using Workslip.Infrastructure.Schema;

namespace Workslip.Api.Configuration;

public static class EndpointConfiguration
{
    public static WebApplication ConfigureEndpoints(this WebApplication app)
    {
        app.MapGet("/health", (HttpContext httpContext) =>
        {
            HttpCacheHeaders.SetPublicHealthCache(httpContext);
            return Results.Ok(new { status = "ok" });
        });

        app.MapOrganizationEndpoints();
        app.MapAuthEndpoints();
        app.MapUserEndpoints();
        app.MapJobEndpoints();
        app.MapCustomerEndpoints();
        app.MapJobLinkEndpoints();
        app.MapWorkSheetEndpoints();
        app.MapReferenceDataEndpoints();

        return app;
    }
}
