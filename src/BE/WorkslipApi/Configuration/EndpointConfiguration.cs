using Scalar.AspNetCore;
using Workslip.Api.Endpoints;
using Workslip.Infrastructure;
using Workslip.Infrastructure.Schema;

namespace Workslip.Api.Configuration;

public static class EndpointConfiguration
{
    public static WebApplication ConfigureEndpoints(this WebApplication app)
    {
        app.MapGet("/", () => Results.Ok(new
        {
            service = "Workslip.Api",
            status = "ok",
            health = "/health"
        })).ExcludeFromDescription();

        app.MapGet("/health", (HttpContext httpContext) =>
        {
            HttpCacheHeaders.SetPublicHealthCache(httpContext);
            return Results.Ok(new { status = "ok" });
        });

        app.MapOrganizationEndpoints();
        app.MapAuthEndpoints();
        app.MapUserEndpoints();
        app.MapJobEndpoints();
        app.MapJobConversationEndpoints();
        app.MapJobOverviewEndpoints();
        app.MapImageEndpoints();
        app.MapCustomerEndpoints();
        app.MapDocumentEndpoints();
        app.MapInventoryEndpoints();
        app.MapJobLinkEndpoints();
        app.MapWorkSheetEndpoints();
        app.MapPowerBiOverviewEndpoints();
        app.MapLeaderAnalysisEndpoints();
        app.MapReferenceDataEndpoints();
        app.MapPushNotificationEndpoints();
        app.MapCacheEndpoints();
        app.MapDiagnosticsEndpoints();
        app.MapControlCenterEndpoints();

        return app;
    }
}
