using Microsoft.AspNetCore.Mvc;
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
        var (readGroup, userGroup) = app.MapReadUserGroups("/api/jobs", "jobs");
        var adminGroup = app.MapAdminGroup("/api/jobs", "jobs");

        readGroup.MapGet("/", async (
            [FromQuery(Name = "status")] JobStatus[]? statuses,
            [FromQuery] string? reportNumber,
            [FromQuery] string? customerName,
            [FromQuery] string? customerEmail,
            [FromQuery] string? customerAddress,
            [FromQuery] string? search,
            [FromQuery] string? sortBy,
            [FromQuery] string? sortDirection,
            [FromQuery] int? limit,
            [FromQuery] int? offset,
            HttpContext httpContext,
            ICurrentUserContext currentUser,
            IJobService service,
            CancellationToken cancellationToken) =>
        {
            var statusList = statuses?.ToList();
            var result = await service.ListAsync(statusList, reportNumber, customerName, customerEmail, customerAddress, search, sortBy, sortDirection, limit, offset, cancellationToken);
            return CachedOk(result, httpContext,
                response => HttpCacheHeaders.JobListEtag(response, currentUser.OrganizationId!.Value, currentUser.UserId, statusList, reportNumber, customerName, customerEmail, customerAddress, search, sortBy, sortDirection, limit, offset),
                response => new {
                    items = response.Items.Select(JobViewModelBuilder.ToListItem).ToArray(),
                    totalCount = response.TotalCount
                });
        });

        readGroup.MapGet("/my-assigned", async (HttpContext httpContext, ICurrentUserContext currentUser, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetMyAssignedJobsAsync(cancellationToken);
            return CachedOk(result, httpContext,
                jobs => HttpCacheHeaders.JobAssignedEtag(jobs, currentUser.OrganizationId!.Value),
                jobs => jobs.Select(JobViewModelBuilder.ToListItem).ToArray());
        }).Produces<List<JobListItemViewModel>>();

        readGroup.MapGet("/{id:guid}", async (Guid id, HttpContext httpContext, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetSingleJobAsync(id, cancellationToken);
            return CachedOk(result, httpContext, report => HttpCacheHeaders.JobReportEtag(report), JobViewModelBuilder.ToSummary);
        }).Produces<JobReportSummaryViewModel>(StatusCodes.Status200OK);

        readGroup.MapGet("/{id:guid}/history", async (Guid id, int? limit, int? offset, HttpContext httpContext, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetHistoryAsync(id, limit, offset, cancellationToken);
            return CachedOk(result, httpContext, events => HttpCacheHeaders.JobHistoryEtag(id, events, limit, offset));
        }).Produces<List<JobHistoryResponse>>(StatusCodes.Status200OK);

        readGroup.MapGet("/{id:guid}/report/pdf", async (Guid id, HttpContext httpContext, IJobService service, IJobReportPdfService pdfService, CancellationToken cancellationToken) =>
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


        userGroup.MapPatch("/{id:guid}", async (Guid id, UpdateJobRequest request, HttpContext httpContext, ICurrentUserContext currentUser, IdempotencyStore idempotency, IJobService service, CancellationToken cancellationToken) =>
        {
            if (!IdempotencyHttp.TryGetKey(httpContext, out var key))
                return Results.StatusCode(StatusCodes.Status428PreconditionRequired);
            var reservation = await idempotency.StartAsync($"jobs.update:{currentUser.OrganizationId}:{currentUser.UserId}:{id}", key, request, cancellationToken);
            var replay = IdempotencyHttp.ReplayOrReject(reservation);
            if (replay is not null) return replay;

            try
            {
                var result = await service.UpdateAsync(id, request, cancellationToken);
                if (result.IsSuccess)
                    await idempotency.CompleteAsync(reservation.Reservation!.Id, reservation.ReservationToken!, JobViewModelBuilder.ToSummary(result.Value), StatusCodes.Status200OK, cancellationToken);
                else
                    await idempotency.AbortAsync(reservation.Reservation!.Id, reservation.ReservationToken!, cancellationToken);
                return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToSummary);
            }
            catch
            {
                await idempotency.AbortAsync(reservation.Reservation!.Id, reservation.ReservationToken!, CancellationToken.None);
                throw;
            }
        }).Produces<JobReportSummaryViewModel>(StatusCodes.Status200OK);

        userGroup.MapPost("/{id:guid}/status", async (Guid id, ChangeJobStatusRequest request, HttpContext httpContext, ICurrentUserContext currentUser, IdempotencyStore idempotency, IJobService service, CancellationToken cancellationToken) =>
        {
            if (!IdempotencyHttp.TryGetKey(httpContext, out var key)) return Results.StatusCode(StatusCodes.Status428PreconditionRequired);
            var reservation = await idempotency.StartAsync($"jobs.status:{currentUser.OrganizationId}:{currentUser.UserId}:{id}", key, request, cancellationToken);
            var replay = IdempotencyHttp.ReplayOrReject(reservation); if (replay is not null) return replay;
            try
            {
                var result = await service.ChangeStatusAsync(id, request, cancellationToken);
                if (result.IsSuccess)
                    await idempotency.CompleteAsync(reservation.Reservation!.Id, reservation.ReservationToken!, JobViewModelBuilder.ToSummary(result.Value), StatusCodes.Status200OK, cancellationToken);
                else
                    await idempotency.AbortAsync(reservation.Reservation!.Id, reservation.ReservationToken!, cancellationToken);
                return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToSummary);
            }
            catch
            {
                await idempotency.AbortAsync(reservation.Reservation!.Id, reservation.ReservationToken!, CancellationToken.None);
                throw;
            }
        }).Produces<JobReportSummaryViewModel>(StatusCodes.Status200OK);


        userGroup.MapPost("/", async (CreateJobRequest request, HttpContext httpContext, ICurrentUserContext currentUser, IdempotencyStore idempotency, IJobService service, CancellationToken cancellationToken) =>
        {
            if (!IdempotencyHttp.TryGetKey(httpContext, out var key))
                return Results.StatusCode(StatusCodes.Status428PreconditionRequired);
            var reservation = await idempotency.StartAsync($"jobs.create:{currentUser.OrganizationId}:{currentUser.UserId}", key, request, cancellationToken);
            var replay = IdempotencyHttp.ReplayOrReject(reservation);
            if (replay is not null) return replay;

            try
            {
                var result = await service.CreateAsync(request, cancellationToken);
                if (result.IsSuccess)
                    await idempotency.CompleteAsync(reservation.Reservation!.Id, reservation.ReservationToken!, JobViewModelBuilder.ToSummary(result.Value), StatusCodes.Status200OK, cancellationToken);
                else
                    await idempotency.AbortAsync(reservation.Reservation!.Id, reservation.ReservationToken!, cancellationToken);
                return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToSummary);
            }
            catch
            {
                await idempotency.AbortAsync(reservation.Reservation!.Id, reservation.ReservationToken!, CancellationToken.None);
                throw;
            }
        }).Produces<JobReportSummaryViewModel>(StatusCodes.Status200OK);

        userGroup.MapPost("/{id:guid}/seen", async (Guid id, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.MarkJobAsSeenAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result);
        }).Produces(StatusCodes.Status204NoContent);

        adminGroup.MapDelete("/{id:guid}", async (Guid id, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.DeleteAsync(id, cancellationToken);
            return result.Status == Ardalis.Result.ResultStatus.Conflict
                ? Results.Conflict(JobDeleteErrorResponse.FromConflictError(result.Errors.FirstOrDefault()))
                : ResultExtensions.ToHttpResult(result);
        })
        .Produces(StatusCodes.Status204NoContent)
        .Produces<JobDeleteErrorResponse>(StatusCodes.Status409Conflict);

        adminGroup.MapPost("/{id:guid}/restore/deletion", async (Guid id, IJobService service, CancellationToken cancellationToken) =>
        {
            var result = await service.RestoreDeletionAsync(id, cancellationToken);
            return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToSummary);
        }).Produces<JobReportSummaryViewModel>(StatusCodes.Status200OK);

        userGroup.MapPost("/{id:guid}/assign", async (Guid id, AssignJobRequest request, IJobService jobService, CancellationToken cancellationToken) =>
        {
            var result = await jobService.AssignAsync(id, request.UserIds, cancellationToken);
            return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToSummary);
        })
        .Produces<JobReportSummaryViewModel>(StatusCodes.Status200OK);

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