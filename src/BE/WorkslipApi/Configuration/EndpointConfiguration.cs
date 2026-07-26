using Microsoft.EntityFrameworkCore;
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
        app.MapGet("/health/ready", CheckDatabaseReadinessAsync);

        app.MapOrganizationEndpoints();
        app.MapAuthEndpoints();
        app.MapUserEndpoints();
        app.MapJobEndpoints();
        app.MapCustomerEndpoints();
        app.MapJobLinkEndpoints();
        app.MapWorkSheetEndpoints();
        app.MapReferenceDataEndpoints();
        app.MapPushNotificationEndpoints();
        app.MapCacheEndpoints();

        return app;
    }

    private static async Task<IResult> CheckDatabaseReadinessAsync(
        HttpContext httpContext,
        SqlDbContext db,
        CancellationToken cancellationToken)
    {
        HttpCacheHeaders.SetNoStore(httpContext);

        var canConnect = await db.Database.CanConnectAsync(cancellationToken);
        return canConnect
            ? Results.Ok(new { status = "ready" })
            : Results.Json(
                new { status = "not_ready" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
