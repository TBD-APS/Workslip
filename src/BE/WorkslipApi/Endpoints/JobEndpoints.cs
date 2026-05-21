using Workslip.Application.Jobs;
using Workslip.Domain;

namespace Workslip.Api.Endpoints;

public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/jobs").WithTags("jobs");

        group.MapPost("/", async (
            CreateJobRequest request,
            IJobRepository repository,
            CancellationToken cancellationToken) =>
        {
            var errors = JobRequestValidator.ValidateCreate(request);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors
                    .GroupBy(error => error.Field)
                    .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray()));
            }

            var created = await repository.CreateAsync(request, cancellationToken);
            return Results.Created($"/api/jobs/{created.Id}", created);
        });

        group.MapGet("/", async (
            Guid? organizationId,
            JobStatus? status,
            int? limit,
            int? offset,
            IJobRepository repository,
            CancellationToken cancellationToken) =>
        {
            var query = new JobQuery(organizationId, status, Math.Clamp(limit ?? 50, 1, 200), Math.Max(offset ?? 0, 0));
            return Results.Ok(await repository.ListAsync(query, cancellationToken));
        });

        group.MapGet("/{id:guid}", async (Guid id, IJobRepository repository, CancellationToken cancellationToken) =>
        {
            var report = await repository.GetAsync(id, cancellationToken);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateJobRequest request,
            IJobRepository repository,
            CancellationToken cancellationToken) =>
        {
            var updated = await repository.UpdateAsync(id, request, cancellationToken);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        group.MapPost("/{id:guid}/submit", async (
            Guid id,
            IJobRepository repository,
            CancellationToken cancellationToken) =>
        {
            var report = await repository.TransitionAsync(id, JobStatus.Submitted, actorId: null, cancellationToken);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        group.MapPost("/{id:guid}/approve", async (
            Guid id,
            Guid? actorId,
            IJobRepository repository,
            CancellationToken cancellationToken) =>
        {
            var report = await repository.TransitionAsync(id, JobStatus.Approved, actorId, cancellationToken);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        group.MapPost("/{id:guid}/reject", async (
            Guid id,
            Guid? actorId,
            IJobRepository repository,
            CancellationToken cancellationToken) =>
        {
            var report = await repository.TransitionAsync(id, JobStatus.Rejected, actorId, cancellationToken);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });

        return app;
    }
}
