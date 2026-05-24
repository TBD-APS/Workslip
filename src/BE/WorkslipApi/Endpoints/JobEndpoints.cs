using Workslip.Api.Helpers;
using Workslip.Api.Services;
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
            return ResultExtensions.ToHttpResult(result);
        }).RequireAuthorization(AuthPolicies.RequireAdmin);

        group.MapGet("/", async (
            Guid? organizationId,
            JobStatus? status,
            int? limit,
            int? offset,
            HttpContext httpContext,
            IJobService service,
            CancellationToken cancellationToken) =>
        {
            var jobs = await service.ListAsync(organizationId, status, limit, offset, cancellationToken);
            var etag = HttpCacheHeaders.JobListEtag(jobs, organizationId, status, limit, offset);
            HttpCacheHeaders.SetPrivateRevalidation(httpContext, etag);

            return HttpCacheHeaders.MatchesIfNoneMatch(httpContext, etag)
                ? Results.StatusCode(StatusCodes.Status304NotModified)
                : Results.Ok(jobs);
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
                    : Results.Ok(result.Value);
            }

            return ResultExtensions.ToHttpResult(result);
        });

        group.MapGet("/{id:guid}/report", async (Guid id, IJobService service, IJobReportPdfService pdfService, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(id, cancellationToken);
            if (!result.IsSuccess)
                return Results.NotFound();

            var pdf = pdfService.Generate(result.Value, result.Value.Status);
            return Results.File(pdf, "application/pdf", $"rapport-{result.Value.ReportNumber}.pdf");
        });

        group.MapPatch("/{id:guid}", async (Guid id, UpdateJobRequest request, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(id, request, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        group.MapPost("/{id:guid}/submit", async (Guid id, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.SubmitAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        group.MapPost("/{id:guid}/approve", async (Guid id, Guid? actorId, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.ApproveAsync(id, actorId, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        group.MapPost("/{id:guid}/reject", async (Guid id, Guid? actorId, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.RejectAsync(id, actorId, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        group.MapDelete("/{id:guid}", async (Guid id, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        }).RequireAuthorization(AuthPolicies.RequireAdmin);

        group.MapPost("/{id:guid}/assign", async (Guid id, Guid? userId, IJobService jobService, CancellationToken cancellationToken) =>
        {
            
            var result = await jobService.AssignAsync(id, userId, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        }).RequireAuthorization(AuthPolicies.RequireAdmin);

        group.MapPost("/{id:guid}/links", async (Guid id, CreateJobLinkRequest request, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateLinkAsync(id, request, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        group.MapGet("/{id:guid}/links", async (Guid id, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetLinksAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        group.MapDelete("/{id:guid}/links/{linkId:guid}", async (Guid id, Guid linkId, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteLinkAsync(id, linkId, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        });

        return app;
    }
}
