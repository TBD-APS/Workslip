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
            return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToSummary);
        }).Produces<JobReportSummaryViewModel>().RequireAuthorization(AuthPolicies.RequireAdmin);

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
            return CachedOk(result, httpContext,
                jobs => HttpCacheHeaders.JobListEtag(jobs, currentUser.OrganizationId!.Value, status, reportNumber, customerName, customerEmail, customerAddress, limit, offset),
                jobs => jobs.Select(JobViewModelBuilder.ToListItem).ToArray());
        });

        group.MapGet("/my-assigned", async (HttpContext httpContext, ICurrentUserContext currentUser, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetMyAssignedJobsAsync(cancellationToken);
            return CachedOk(result, httpContext,
                jobs => HttpCacheHeaders.JobAssignedEtag(jobs, currentUser.OrganizationId!.Value),
                jobs => jobs.Select(JobViewModelBuilder.ToListItem).ToArray());
        });

        group.MapGet("/{id:guid}", async (Guid id, HttpContext httpContext, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetSingleJobAsync(id, cancellationToken);
            return CachedOk(result, httpContext, report => HttpCacheHeaders.JobReportEtag(report), JobViewModelBuilder.ToSummary);
        }).Produces<JobReportSummaryViewModel>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}/history", async (Guid id, int? limit, int? offset, HttpContext httpContext, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetHistoryAsync(id, limit, offset, cancellationToken);
            return CachedOk(result, httpContext, events => HttpCacheHeaders.JobHistoryEtag(id, events, limit, offset));
        }).Produces<JobReportSummaryViewModel>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}/report/pdf", async (Guid id, HttpContext httpContext, IJobService service, IJobReportPdfService pdfService, CancellationToken cancellationToken) =>
        {
            var result = await service.GetSingleJobAsync(id, cancellationToken);
            if (!result.IsSuccess)
                return ResultExtensions.ToHttpResult(result);

            HttpCacheHeaders.SetNoStore(httpContext);
            var jobBaseUri = new Uri($"{httpContext.Request.Scheme}://{httpContext.Request.Host}/api/jobs/");
            var pdf = pdfService.Generate(result.Value, result.Value.Status, jobBaseUri);
            var reportNumber = string.IsNullOrWhiteSpace(result.Value.ReportNumber)
                ? result.Value.Id.ToString("N")[..8]
                : result.Value.ReportNumber;

            return Results.File(pdf, "application/pdf", $"rapport-{reportNumber}.pdf");
        });

        group.MapPatch("/{id:guid}", async (Guid id, UpdateJobRequest request, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(id, request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToSummary);
        }).Produces<JobReportSummaryViewModel>(StatusCodes.Status200OK);

        group.MapPost("/{id:guid}/status", async (Guid id, ChangeJobStatusRequest request, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.ChangeStatusAsync(id, request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToSummary);
        }).Produces<JobReportSummaryViewModel>(StatusCodes.Status200OK);

        group.MapDelete("/{id:guid}", async (Guid id, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        }).RequireAuthorization(AuthPolicies.RequireAdmin);

        group.MapPost("/{id:guid}/restore/deletion", async (Guid id, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.RestoreDeletionAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToSummary);
        }).Produces<JobReportSummaryViewModel>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireAdmin);

        group.MapPost("/{id:guid}/assign", async (Guid id, AssignJobRequest request, IJobService jobService, CancellationToken cancellationToken) =>
        {
            var result = await jobService.AssignAsync(id, request.UserIds, cancellationToken);
            return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToSummary);
        })
        .Produces<JobReportSummaryViewModel>(StatusCodes.Status200OK)
        .RequireAuthorization(AuthPolicies.RequireAdmin);

        return app;
    }

    private static IResult CachedOk<T>(
        Ardalis.Result.Result<T> result,
        HttpContext httpContext,
        Func<T, string> etagFactory,
        Func<T, object?>? map = null)
    {
        if (!result.IsSuccess)
            return ResultExtensions.ToHttpResult(result);

        var etag = etagFactory(result.Value);
        HttpCacheHeaders.SetPrivateRevalidation(httpContext, etag);

        return HttpCacheHeaders.MatchesIfNoneMatch(httpContext, etag)
            ? TypedResults.StatusCode(StatusCodes.Status304NotModified)
            : TypedResults.Ok(map?.Invoke(result.Value) ?? result.Value);
    }
}
