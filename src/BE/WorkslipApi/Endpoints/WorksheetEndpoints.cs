using Microsoft.AspNetCore.Mvc;
using Workslip.Api.Helpers;
using Workslip.Api.ViewModels;
using Workslip.Application.Jobs;
using Workslip.Application.Worksheets;

namespace Workslip.Api.Endpoints
{
    public static class WorksheetEndpoints
    {
        public static IEndpointRouteBuilder MapWorkSheetEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/worksheets").WithTags("worksheet").RequireAuthorization(AuthPolicies.RequireUser);

            group.MapPost("/jobs/{jobId:guid}", async (Guid jobId, UpsertWorksheetRequest request, IWorksheetService service, CancellationToken cancellationToken) =>
            {
                var upsertRequest = request with { JobId = jobId };
                var result = await service.UpsertAsync(upsertRequest, cancellationToken);
                return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToSummary);
            }).Produces<JobReportSummaryViewModel>(StatusCodes.Status200OK);

            group.MapDelete("{worksheetId}/jobs/{jobId}", async (Guid worksheetId, Guid jobId, IWorksheetService service, CancellationToken cancellationToken) =>
            {
                var result = await service.DeleteAsync(worksheetId, jobId, cancellationToken);
                return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToSummary);
            }).Produces<JobReportSummaryViewModel>(StatusCodes.Status200OK);

            group.MapGet("/my", async ([FromQuery] int? year, [FromQuery] int? month, IWorksheetService service, CancellationToken cancellationToken) =>
            {
                var result = await service.GetWorksheetsForUserAsync(year, month, cancellationToken);
                return ResultExtensions.ToHttpResult(result);
            }).Produces<MyWorksheetsMonthResponse>(StatusCodes.Status200OK);

            group.MapGet("/all", async ([FromQuery] int? year, [FromQuery] int? month, IWorksheetService service, CancellationToken cancellationToken) =>
            {
                var result = await service.GetAllWorksheetsAsync(year, month, cancellationToken);
                return ResultExtensions.ToHttpResult(result);
            }).Produces<MyWorksheetsMonthResponse>(StatusCodes.Status200OK).RequireAuthorization(AuthPolicies.RequireAdmin);

            return app;
        }
    }
}
