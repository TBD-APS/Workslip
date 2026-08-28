using Microsoft.AspNetCore.Mvc;
using Workslip.Api.Helpers;
using Workslip.Application.LeaderAnalysis;

namespace Workslip.Api.Endpoints;

public static class LeaderAnalysisEndpoints
{
    public static IEndpointRouteBuilder MapLeaderAnalysisEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/leader-analysis").RequireAuthorization("RequireAdmin").WithTags("leader-analysis");

        group.MapGet("/economics", async (
            [FromQuery] string? startDate,
            [FromQuery] string? endDate,
            [FromServices] ILeaderEconomicsService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.GetDocumentsAsync(startDate, endDate, cancellationToken);
            return Results.Ok(result);
        }).Produces<LeaderEconomicsResponse>();

        group.MapGet("/economics/summary", async (
            [FromQuery] string? startDate,
            [FromQuery] string? endDate,
            [FromServices] ILeaderEconomicsService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.GetSummaryAsync(startDate, endDate, cancellationToken);
            return Results.Ok(result);
        }).Produces<LeaderEconomicsSummaryResponse>();

        return app;
    }
}
