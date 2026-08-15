using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
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

            group.MapGet("/all/report/power-bi", (IConfiguration configuration, HttpContext httpContext) =>
            {
                HttpCacheHeaders.SetNoStore(httpContext);
                return Results.Ok(new { url = GetPowerBiReportUrl(configuration["PowerBiReport:Url"]) });
            })
            .Produces(StatusCodes.Status200OK)
            .RequireAuthorization(AuthPolicies.RequireAdmin);

            group.MapGet("/all/report/pdf", async (
                [FromQuery] int? year,
                [FromQuery] int? month,
                HttpContext httpContext,
                IWorksheetService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetAllWorksheetsPdfAsync(year, month, cancellationToken);
                if (!result.IsSuccess)
                    return ResultExtensions.ToHttpResult(result);

                HttpCacheHeaders.SetNoStore(httpContext);
                return Results.File(result.Value.Content, "application/pdf", result.Value.FileName);
            })
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthPolicies.RequireAdmin);

            group.MapGet("/all/report/pdf/preview", async (
                [FromQuery] int? year,
                [FromQuery] int? month,
                HttpContext httpContext,
                IWorksheetService service,
                CancellationToken cancellationToken) =>
            {
                var result = await service.GetAllWorksheetsPdfPreviewAsync(year, month, cancellationToken);
                if (!result.IsSuccess)
                    return ResultExtensions.ToHttpResult(result);

                HttpCacheHeaders.SetNoStore(httpContext);
                return Results.Ok(result.Value);
            })
            .Produces<MonthlyHoursPdfPreviewResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(AuthPolicies.RequireAdmin);

            return app;
        }

        private static string? GetPowerBiReportUrl(string? configuredUrl)
        {
            if (string.IsNullOrWhiteSpace(configuredUrl)
                || !Uri.TryCreate(configuredUrl.Trim(), UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(uri.Host, "app.powerbi.com", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return uri.AbsoluteUri;
        }
    }
}
