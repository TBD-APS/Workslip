using Workslip.Api.Helpers;
using Workslip.Api.ViewModels;
using Workslip.Application.Jobs;

namespace Workslip.Api.Endpoints;

public static class JobOverviewEndpoints
{
    public static IEndpointRouteBuilder MapJobOverviewEndpoints(this IEndpointRouteBuilder app)
    {
        var (readGroup, _) = app.MapReadUserGroups("/api/jobs", "jobs-overview");

        readGroup.MapGet("/overview", async (
            HttpContext httpContext,
            IJobOverviewService service,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.GetAsync(cancellationToken);
            return ResultExtensions.ToHttpResult(result, overview => new JobOverviewViewModel(
                overview.ActiveCount,
                overview.InReviewCount,
                overview.ApprovedCount,
                overview.RejectedCount,
                overview.RecentJobs.Select(JobViewModelBuilder.ToListItem).ToArray()));
        }).Produces<JobOverviewViewModel>(StatusCodes.Status200OK);

        return app;
    }
}
