using Microsoft.AspNetCore.Mvc;
using Workslip.Api.Helpers;
using Workslip.Api.ViewModels;
using Workslip.Application.Jobs;

namespace Workslip.Api.Endpoints
{
    public static class JobLinksEndpoints
    {
        public static IEndpointRouteBuilder MapJobLinkEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapUserGroup("/api/jobs", "jobs");

            group.MapPost("/{id:guid}/links", async (Guid id, [FromBody] CreateJobLinkRequest request, IJobService service, CancellationToken cancellationToken) =>
            {
                var result = await service.CreateLinksAsync(id, request, cancellationToken);
                return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToSummary);
            }).Produces<JobReportSummaryViewModel>();

            group.MapDelete("/{id:guid}/links", async (Guid id, [FromBody] DeleteJobLinksRequest request, IJobService service, CancellationToken cancellationToken) =>
            {
                var result = await service.DeleteLinksAsync(id, request, cancellationToken);
                return ResultExtensions.ToHttpResult(result);
            });

            return app;
        }
    }
}