using Workslip.Api.Helpers;
using Workslip.Application.Worksheets;

namespace Workslip.Api.Endpoints
{
    public static class WorksheetEndpoints
    {
        public static IEndpointRouteBuilder MapWorkSheetEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/worksheet").WithTags("worksheet");

            group.MapPost("/organization/{organizationId}/jobs/{jobId}/worksheets", async (CreateWorksheetRequest request, IWorksheetService service, CancellationToken cancellationToken) =>
            {
                var result = await service.CreateAsync(request, cancellationToken);
                return ResultExtensions.ToHttpResult(result);
            });

            return app;
        }
    }
}
