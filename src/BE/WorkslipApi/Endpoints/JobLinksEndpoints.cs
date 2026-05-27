using Workslip.Api.Helpers;
using Workslip.Api.ViewModels;
using Workslip.Application.Jobs;

namespace Workslip.Api.Endpoints
{
    public static class JobLinksEndpoints
    {
        public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/jobs").WithTags("jobs").RequireAuthorization(AuthPolicies.RequireUser);

            group.MapPost("/{id:guid}/links", async (Guid id, CreateJobLinkRequest request, IJobService service, CancellationToken cancellationToken) =>
            {
                var result = await service.CreateLinkAsync(id, request, cancellationToken);
                return ResultExtensions.ToHttpResult(result, JobViewModelBuilder.ToLink);
            });

            group.MapGet("/{id:guid}/links", async (Guid id, IJobService service, CancellationToken cancellationToken) =>
            {
                var result = await service.GetLinksAsync(id, cancellationToken);
                return ResultExtensions.ToHttpResult(result, links => links.Select(JobViewModelBuilder.ToLink).ToArray());
            });

            group.MapDelete("/{id:guid}/links/{linkId:guid}", async (Guid id, Guid linkId, IJobService service, CancellationToken cancellationToken) =>
            {
                var result = await service.DeleteLinkAsync(id, linkId, cancellationToken);
                return ResultExtensions.ToHttpResult(result);
            });


            return app;

        }
    }
}