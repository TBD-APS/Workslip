using Workslip.Api.Helpers;
using Workslip.Application.Diagnostics;

namespace Workslip.Api.Endpoints;

public static class DiagnosticsEndpoints
{
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/diagnostics")
            .WithTags("diagnostics")
            .RequireAuthorization(AuthPolicies.RequireSuperAdmin)
            .RequireRateLimiting("diagnostics-read");

        group.MapGet("/errors", async (
            string? range,
            string? source,
            int? limit,
            HttpContext httpContext,
            IErrorDiagnosticsService service,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.GetAsync(
                new ErrorDiagnosticsQuery(
                    range ?? "24h",
                    source ?? "all",
                    limit ?? 50),
                cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        })
        .Produces<ErrorDiagnosticsDashboard>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests);

        return app;
    }
}
