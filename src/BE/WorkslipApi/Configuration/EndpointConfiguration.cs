using Scalar.AspNetCore;
using Workslip.Api.Endpoints;

namespace Workslip.Api.Configuration;

public static class EndpointConfiguration
{
    public static WebApplication ConfigureEndpoints(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
        }

        app.MapGet("/health", (HttpContext httpContext) =>
        {
            HttpCacheHeaders.SetPublicHealthCache(httpContext);
            return Results.Ok(new { status = "ok" });
        });

        app.MapOrganizationEndpoints();
        app.MapAuthEndpoints();
        app.MapUserEndpoints();
        app.MapJobEndpoints();

        return app;
    }
}
