using Workslip.Api.Helpers;
using Workslip.Application.Jobs;

namespace Workslip.Api.Endpoints;

public static class ReferenceDataEndpoints
{
    public static IEndpointRouteBuilder MapReferenceDataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reference-data")
            .WithTags("reference-data")
            .RequireAuthorization(AuthPolicies.RequireUser);

        group.MapGet("/", async (HttpContext httpContext, IReferenceDataService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(cancellationToken);
            if (!result.IsSuccess)
                return ResultExtensions.ToHttpResult(result);

            var etag = HttpCacheHeaders.ReferenceDataEtag(result.Value);
            HttpCacheHeaders.SetPrivateRevalidation(httpContext, etag);

            return HttpCacheHeaders.MatchesIfNoneMatch(httpContext, etag)
                ? Results.StatusCode(StatusCodes.Status304NotModified)
                : Results.Ok(result.Value);
        }).Produces<ReferenceDataResponse>(StatusCodes.Status200OK);

        return app;
    }
}
