using ArdalisResultStatus = Ardalis.Result.ResultStatus;
using Workslip.Api.Helpers;
using Workslip.Api.Services;
using Workslip.Api.ViewModels;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Domain;

namespace Workslip.Api.Endpoints;

public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/jobs").WithTags("jobs").RequireAuthorization(AuthPolicies.RequireUser);

        group.MapPost("/", async (CreateJobRequest request, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToJob);
        }).RequireAuthorization(AuthPolicies.RequireAdmin);

        group.MapGet("/", async (JobStatus? status,
            string? reportNumber,
            string? customerName,
            string? customerEmail,
            string? customerAddress,
            int? limit,
            int? offset,
            HttpContext httpContext,
            ICurrentUserContext currentUser,
            IJobService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListAsync(status, reportNumber, customerName, customerEmail, customerAddress, limit, offset, cancellationToken);
            if (!result.IsSuccess)
            {
                return ResultExtensions.ToHttpResult(result);
            }

            var jobs = result.Value;
            var etag = HttpCacheHeaders.JobListEtag(jobs, currentUser.OrganizationId!.Value, status, reportNumber, customerName, customerEmail, customerAddress, limit, offset);
            HttpCacheHeaders.SetPrivateRevalidation(httpContext, etag);

            return HttpCacheHeaders.MatchesIfNoneMatch(httpContext, etag)
                ? Results.StatusCode(StatusCodes.Status304NotModified)
                : Results.Ok(jobs.Select(JobViewModelBuilder.ToListItem).ToArray());
        });

        group.MapGet("/{id:guid}", async (Guid id, HttpContext httpContext, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(id, cancellationToken);
            if (result.IsSuccess)
            {
                var etag = HttpCacheHeaders.JobReportEtag(result.Value);
                HttpCacheHeaders.SetPrivateRevalidation(httpContext, etag);

                return HttpCacheHeaders.MatchesIfNoneMatch(httpContext, etag)
                    ? Results.StatusCode(StatusCodes.Status304NotModified)
                    : Results.Ok(JobViewModelBuilder.ToJob(result.Value));
            }

            return ResultExtensions.ToHttpResult(result);
        });

        group.MapGet("/{id:guid}/report-summary", async (Guid id, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetReportSummaryAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToSummary);
        });

        group.MapGet("/{id:guid}/history", async (Guid id, int? limit, int? offset, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetHistoryAsync(id, limit, offset, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        group.MapGet("/{id:guid}/report/pdf", async (Guid id, IJobService service, IJobReportPdfService pdfService, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(id, cancellationToken);
            if (!result.IsSuccess)
            {
                return result.Status switch
                {
                    ArdalisResultStatus.Unauthorized => Results.Unauthorized(),
                    ArdalisResultStatus.Forbidden => Results.Forbid(),
                    _ => Results.NotFound()
                };
            }

            var pdf = pdfService.Generate(result.Value, result.Value.Status);
            return Results.File(pdf, "application/pdf", $"rapport-{result.Value.ReportNumber}.pdf");
        });

        group.MapPatch("/{id:guid}", async (Guid id, UpdateJobRequest request, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(id, request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToJob);
        });

        group.MapPost("/{id:guid}/status", async (Guid id, ChangeJobStatusRequest request, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.ChangeStatusAsync(id, request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToJob);
        });

        group.MapDelete("/{id:guid}", async (Guid id, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToJob);
        }).RequireAuthorization(AuthPolicies.RequireAdmin);

        group.MapPost("/{id:guid}/restore/deletion", async (Guid id, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.RestoreDeletionAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToJob);
        }).RequireAuthorization(AuthPolicies.RequireAdmin);

        group.MapPost("/{id:guid}/assign", async (Guid id, AssignJobRequest request, IJobService jobService, CancellationToken cancellationToken) =>
        {
            var result = await jobService.AssignAsync(id, request.UserIds, cancellationToken);
            return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToJob);
        }).RequireAuthorization(AuthPolicies.RequireAdmin);

       
        return app;
    }
}
