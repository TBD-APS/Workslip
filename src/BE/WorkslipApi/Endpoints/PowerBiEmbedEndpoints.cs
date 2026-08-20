using Microsoft.Extensions.Configuration;
using Workslip.Api.Helpers;

namespace Workslip.Api.Endpoints;

public static class PowerBiEmbedEndpoints
{
    public static IEndpointRouteBuilder MapPowerBiEmbedEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/power-bi/report", (IConfiguration configuration, HttpContext httpContext) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var report = PowerBiReportUrlResolver.Resolve(configuration["PowerBiReport:Url"]);
            return Results.Ok(new
            {
                url = report?.Url,
                embedUrl = report?.EmbedUrl,
            });
        })
        .WithTags("power-bi")
        .Produces(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireAdmin);

        return app;
    }
}
