using Workslip.Api.Helpers;
using Workslip.Application.Worksheets;

namespace Workslip.Api.Endpoints
{
    public static class WorksheetEndpoints
    {
        public static IEndpointRouteBuilder MapWorkSheetEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/worksheets").WithTags("worksheet");

            group.MapPost("/jobs/{jobId}", async (CreateWorksheetRequest request, IWorksheetService service, CancellationToken cancellationToken) =>
            {
                var result = await service.CreateAsync(request, cancellationToken);
                return ResultExtensions.ToHttpResult(result);
            });

            group.MapDelete("{worksheetId}/jobs/{jobId}", async (Guid worksheetId, Guid jobId, IWorksheetService service, CancellationToken cancellationToken) =>
            {
                var result = await service.DeleteAsync(worksheetId, jobId, cancellationToken);
                return ResultExtensions.ToHttpResult(result);
            });

            return app;
        }
    }
}
