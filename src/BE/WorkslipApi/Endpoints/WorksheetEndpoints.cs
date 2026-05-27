using Workslip.Api.Helpers;
using Workslip.Application.Worksheets;

namespace Workslip.Api.Endpoints
{
    public static class WorksheetEndpoints
    {
        public static IEndpointRouteBuilder MapWorkSheetEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/worksheets").WithTags("worksheet").RequireAuthorization(AuthPolicies.RequireUser); ;

            group.MapGet("/jobs/{jobId:guid}", async (Guid jobId, IWorksheetService service, CancellationToken cancellationToken) =>
            {
                var result = await service.ListByJobAsync(jobId, cancellationToken);
                return ResultExtensions.ToHttpResult(result);
            });

            group.MapPost("/jobs/{jobId:guid}", async (Guid jobId, CreateWorksheetRequest request, IWorksheetService service, CancellationToken cancellationToken) =>
            {
                var upsertRequest = request with { JobId = jobId };
                var result = await service.UpsertAsync(upsertRequest, cancellationToken);
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
