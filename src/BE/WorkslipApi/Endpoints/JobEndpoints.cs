using Workslip.Application.Jobs;
using Workslip.Domain;

namespace Workslip.Api.Endpoints;

public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/jobs").WithTags("jobs");

        group.MapPost("/", async (CreateJobRequest request, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken);
            return ToCreatedResult(result);
        });

        group.MapGet("/", async (
            Guid? organizationId,
            JobStatus? status,
            int? limit,
            int? offset,
            IJobService service,
            CancellationToken cancellationToken) =>
        {
            var jobs = await service.ListAsync(organizationId, status, limit, offset, cancellationToken);
            return Results.Ok(jobs);
        });

        group.MapGet("/{id:guid}", async (Guid id, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(id, cancellationToken);
            return ToOkResult(result);
        });

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateJobRequest request,
            IJobService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateAsync(id, request, cancellationToken);
            return ToOkResult(result);
        });

        group.MapPost("/{id:guid}/submit", async (Guid id, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.SubmitAsync(id, cancellationToken);
            return ToOkResult(result);
        });

        group.MapPost("/{id:guid}/approve", async (Guid id, Guid? actorId, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.ApproveAsync(id, actorId, cancellationToken);
            return ToOkResult(result);
        });

        group.MapPost("/{id:guid}/reject", async (Guid id, Guid? actorId, IJobService service, CancellationToken cancellationToken) =>
        {
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
