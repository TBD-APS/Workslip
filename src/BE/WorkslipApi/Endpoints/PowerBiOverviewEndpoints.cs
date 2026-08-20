using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Infrastructure.Schema;

namespace Workslip.Api.Endpoints;

public static class PowerBiOverviewEndpoints
{
    public static IEndpointRouteBuilder MapPowerBiOverviewEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/power-bi/overview/job-status", async (
            HttpContext httpContext,
            ICurrentUserContext currentUser,
            SqlDbContext db,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);

            if (currentUser.OrganizationId is not Guid organizationId)
            {
                return Results.Unauthorized();
            }

            var grouped = await db.JobReports
                .AsNoTracking()
                .Where(job => job.OrganizationId == organizationId && !job.IsSoftDeleted)
                .GroupBy(job => job.Status)
                .Select(group => new
                {
                    Status = group.Key,
                    Count = group.Count(),
                })
                .ToListAsync(cancellationToken);

            int CountStatus(string status) => grouped
                .Where(row => string.Equals(row.Status.ToString(), status, StringComparison.OrdinalIgnoreCase))
                .Sum(row => row.Count);

            var draft = CountStatus("Draft");
            var inReview = CountStatus("InReview");
            var approved = CountStatus("Approved");
            var rejected = CountStatus("Rejected");
            var total = grouped.Sum(row => row.Count);
            var known = draft + inReview + approved + rejected;

            return Results.Ok(new
            {
                total,
                draft,
                inReview,
                approved,
                rejected,
                other = Math.Max(0, total - known),
                generatedAtUtc = DateTimeOffset.UtcNow,
            });
        })
        .WithTags("power-bi")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .RequireAuthorization(AuthPolicies.RequireAdmin);

        return app;
    }
}
