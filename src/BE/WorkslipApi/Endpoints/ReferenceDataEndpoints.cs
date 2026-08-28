using Workslip.Api.Helpers;
using Workslip.Application.Jobs;
using Workslip.Application.ModuleAccess;

namespace Workslip.Api.Endpoints;

public static class ReferenceDataEndpoints
{
    public static IEndpointRouteBuilder MapReferenceDataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reference-data")
            .WithTags("reference-data")
            .RequireAuthorization(AuthPolicies.RequireReadAccess);

        group.MapGet("/", async (HttpContext httpContext, IReferenceDataService service, IWorkslipModuleAccess moduleAccess, CancellationToken cancellationToken) =>
        {
            var result = await service.GetAsync(cancellationToken);
            if (!result.IsSuccess)
                return ResultExtensions.ToHttpResult(result);

            // The installation-type / control-point catalog belongs to the
            // Compliance & Evidence module. When the tenant is not entitled,
            // strip it while keeping the rest of the reference data (work kinds,
            // closure flags) so the non-KLS/simple job flow still works. This is
            // the server-side authority; the UI also hides these surfaces.
            var decision = await moduleAccess.EvaluateAsync(WorkslipModuleKey.ComplianceEvidence, cancellationToken);
            var value = decision.IsEnabled
                ? result.Value
                : result.Value with { InstallationTypes = Array.Empty<InstallationTypeDefinitionResponse>() };

            var etag = HttpCacheHeaders.ReferenceDataEtag(value);
            HttpCacheHeaders.SetPrivateRevalidation(httpContext, etag);

            return HttpCacheHeaders.MatchesIfNoneMatch(httpContext, etag)
                ? Results.StatusCode(StatusCodes.Status304NotModified)
                : Results.Ok(value);
        }).Produces<ReferenceDataResponse>(StatusCodes.Status200OK);

        return app;
    }
}
