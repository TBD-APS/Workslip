using Workslip.Application.Jobs;
using Workslip.Domain;

namespace Workslip.Api.Endpoints;


public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/jobs").WithTags("jobs").RequireAuthorization(AuthPolicies.RequireUser);

        group.MapPost("/", async (CreateJobRequest request, HttpContext httpContext, IJobService service, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.CreateAsync(request, cancellationToken);
            return ToCreatedResult(result);
        }).WithDisplayName("Create job").RequireAuthorization(AuthPolicies.RequireAdmin);

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
        }).WithDisplayName("Get All Jobs");

        group.MapGet("/{id:guid}", async (Guid id, HttpContext httpContext, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(id, cancellationToken);
            if (result.Status == JobServiceResultStatus.Success && result.Value is not null)
            {
                var etag = HttpCacheHeaders.JobReportEtag(result.Value);
                HttpCacheHeaders.SetPrivateRevalidation(httpContext, etag);

                return HttpCacheHeaders.MatchesIfNoneMatch(httpContext, etag)
                    ? Results.StatusCode(StatusCodes.Status304NotModified)
                    : Results.Ok(result.Value);
            }

            HttpCacheHeaders.SetNoStore(httpContext);
            return ToOkResult(result);
        }).WithDisplayName("Get single job");

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateJobRequest request,
            HttpContext httpContext,
            IJobService service,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.UpdateAsync(id, request, cancellationToken);
            return ToOkResult(result);
        }).WithDisplayName("Updte job");

        group.MapPost("/{id:guid}/submit", async (Guid id, HttpContext httpContext, IJobService service, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.SubmitAsync(id, cancellationToken);
            return ToOkResult(result);
        });

        group.MapPost("/{id:guid}/approve", async (Guid id, Guid? actorId, HttpContext httpContext, IJobService service, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.ApproveAsync(id, actorId, cancellationToken);
            return ToOkResult(result);
        });

        group.MapPost("/{id:guid}/reject", async (Guid id, Guid? actorId, HttpContext httpContext, IJobService service, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.RejectAsync(id, actorId, cancellationToken);
            return ToOkResult(result);
        });

        return app;
    }

    private static IResult ToCreatedResult(JobServiceResult<JobReportResponse> result) =>
        result.Status switch
        {
            JobServiceResultStatus.Success when result.Value is not null => Results.Created($"/api/jobs/{result.Value.Id}", result.Value),
            JobServiceResultStatus.ValidationFailed => Results.ValidationProblem(ToProblem(result.Errors)),
            JobServiceResultStatus.NotFound => Results.NotFound(),
            _ => Results.Problem("Unable to create job.")
        };

    private static IResult ToOkResult(JobServiceResult<JobReportResponse> result) =>
        result.Status switch
        {
            JobServiceResultStatus.Success when result.Value is not null => Results.Ok(result.Value),
            JobServiceResultStatus.ValidationFailed => Results.ValidationProblem(ToProblem(result.Errors)),
            JobServiceResultStatus.NotFound => Results.NotFound(),
            _ => Results.Problem("Unable to process job request.")
        };

    private static Dictionary<string, string[]> ToProblem(IEnumerable<JobValidationError> errors) =>
        errors.GroupBy(error => error.Field)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray());
}
