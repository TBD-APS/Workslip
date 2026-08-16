using Ardalis.Result;
using Workslip.Api.Helpers;
using Workslip.Application.Operations;

namespace Workslip.Api.Endpoints;

public static class ControlCenterEndpoints
{
    public static IEndpointRouteBuilder MapControlCenterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/control-center")
            .WithTags("control-center")
            .RequireAuthorization(AuthPolicies.RequireSuperAdmin)
            .RequireRateLimiting("diagnostics-read");

        group.MapGet("/snapshot", async (
            HttpContext httpContext,
            IControlCenterReadService service,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.GetSnapshotAsync(cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        })
        .Produces<ControlCenterSnapshot>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests);

        group.MapGet("/summary", async (
            HttpContext httpContext,
            IControlCenterReadService service,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var snapshotResult = await service.GetSnapshotAsync(cancellationToken);
            if (!snapshotResult.IsSuccess)
            {
                return ResultExtensions.ToHttpResult(snapshotResult);
            }

            var summary = ControlCenterSummaryProjection.FromSnapshot(snapshotResult.Value);
            return ResultExtensions.ToHttpResult(Result<ControlCenterSummary>.Success(summary));
        })
        .Produces<ControlCenterSummary>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests);

        return app;
    }
}
