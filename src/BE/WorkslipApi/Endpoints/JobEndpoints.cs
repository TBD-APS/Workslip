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
            IJobTaxonomyRepository taxonomyRepository,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("Workslip.Api.Endpoints.Jobs");
            var taxonomy = await taxonomyRepository.GetAsync(cancellationToken);
            var errors = JobRequestValidator.ValidateCreate(request, taxonomy);
            if (errors.Count > 0)
            {
                logger.LogWarning("Job create validation failed. OrganizationId: {OrganizationId}. Fields: {ValidationFields}",
                    request.OrganizationId,
                    string.Join(",", errors.Select(error => error.Field).Distinct()));

                return Results.ValidationProblem(ToProblem(errors));
            }

            var created = await repository.CreateAsync(request, cancellationToken);
            logger.LogInformation(
                "Job created. JobId: {JobId}. OrganizationId: {OrganizationId}. Status: {Status}. ReportNumber: {ReportNumber}. WorkKind: {WorkKind}. InstallationTypeCount: {InstallationTypeCount}. ControlInstallationTypeCount: {ControlInstallationTypeCount}.",
                created.Id,
                created.OrganizationId,
                created.Status,
                created.ReportNumber,
                created.WorkKind,
                created.InstallationTypes.Count,
                created.ControlInstallationTypes.Count);

            return Results.Created($"/api/jobs/{created.Id}", created);
        });

        group.MapGet("/", async (
            Guid? organizationId,
            JobStatus? status,
            int? limit,
            int? offset,
            IJobRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("Workslip.Api.Endpoints.Jobs");
            var query = new JobQuery(organizationId, status, Math.Clamp(limit ?? 50, 1, 200), Math.Max(offset ?? 0, 0));
            var jobs = await repository.ListAsync(query, cancellationToken);

            logger.LogInformation("Jobs listed. OrganizationId: {OrganizationId}. StatusFilter: {StatusFilter}. Limit: {Limit}. Offset: {Offset}. ResultCount: {ResultCount}.",
                query.OrganizationId,
                query.Status,
                query.Limit,
                query.Offset,
                jobs.Count);

            return Results.Ok(jobs);
        });

        group.MapGet("/{id:guid}", async (Guid id, IJobRepository repository, ILoggerFactory loggerFactory, CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("Workslip.Api.Endpoints.Jobs");
            var report = await repository.GetAsync(id, cancellationToken);
            if (report is null)
            {
                logger.LogWarning("Job lookup returned not found. JobId: {JobId}.", id);
                return Results.NotFound();
            }

            logger.LogInformation("Job fetched. JobId: {JobId}. OrganizationId: {OrganizationId}. Status: {Status}.",
                report.Id,
                report.OrganizationId,
                report.Status);

            return Results.Ok(report);
        });

        group.MapPatch("/{id:guid}", async (
            Guid id,
            UpdateJobRequest request,
            IJobRepository repository,
            IJobTaxonomyRepository taxonomyRepository,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("Workslip.Api.Endpoints.Jobs");
            var taxonomy = await taxonomyRepository.GetAsync(cancellationToken);
            var errors = JobRequestValidator.ValidateUpdate(request, taxonomy);
            if (errors.Count > 0)
            {
                logger.LogWarning("Job update validation failed. JobId: {JobId}. Fields: {ValidationFields}",
                    id,
                    string.Join(",", errors.Select(error => error.Field).Distinct()));

                return Results.ValidationProblem(ToProblem(errors));
            }

            var updated = await repository.UpdateAsync(id, request, cancellationToken);
            if (updated is null)
            {
                logger.LogWarning("Job update returned not found. JobId: {JobId}.", id);
                return Results.NotFound();
            }

            logger.LogInformation(
                "Job updated. JobId: {JobId}. OrganizationId: {OrganizationId}. Status: {Status}. ReportNumber: {ReportNumber}. WorkKind: {WorkKind}. InstallationTypeCount: {InstallationTypeCount}. ControlInstallationTypeCount: {ControlInstallationTypeCount}.",
                updated.Id,
                updated.OrganizationId,
                updated.Status,
                updated.ReportNumber,
                updated.WorkKind,
                updated.InstallationTypes.Count,
                updated.ControlInstallationTypes.Count);

            return Results.Ok(updated);
        });

        group.MapPost("/{id:guid}/submit", async (
            Guid id,
            IJobRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("Workslip.Api.Endpoints.Jobs");
            var report = await repository.TransitionAsync(id, JobStatus.Submitted, actorId: null, cancellationToken);
            return ToTransitionResult(id, JobStatus.Submitted, actorId: null, report, logger);
        });

        group.MapPost("/{id:guid}/approve", async (
            Guid id,
            Guid? actorId,
            IJobRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("Workslip.Api.Endpoints.Jobs");
            var report = await repository.TransitionAsync(id, JobStatus.Approved, actorId, cancellationToken);
            return ToTransitionResult(id, JobStatus.Approved, actorId, report, logger);
        });

        group.MapPost("/{id:guid}/reject", async (
            Guid id,
            Guid? actorId,
            IJobRepository repository,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("Workslip.Api.Endpoints.Jobs");
            var report = await repository.TransitionAsync(id, JobStatus.Rejected, actorId, cancellationToken);
            return ToTransitionResult(id, JobStatus.Rejected, actorId, report, logger);
        });

        return app;
    }

    private static IResult ToTransitionResult(
        Guid jobId,
        JobStatus targetStatus,
        Guid? actorId,
        JobReportResponse? report,
        ILogger logger)
    {
        if (report is null)
        {
            logger.LogWarning("Job transition returned not found. JobId: {JobId}. TargetStatus: {TargetStatus}. ActorId: {ActorId}.",
                jobId,
                targetStatus,
                actorId);

            return Results.NotFound();
        }

        logger.LogInformation("Job transitioned. JobId: {JobId}. OrganizationId: {OrganizationId}. TargetStatus: {TargetStatus}. ActorId: {ActorId}.",
            report.Id,
            report.OrganizationId,
            targetStatus,
            actorId);

        return Results.Ok(report);
    }

    private static Dictionary<string, string[]> ToProblem(IEnumerable<JobValidationError> errors) =>
        errors.GroupBy(error => error.Field)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray());
}
