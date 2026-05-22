using Workslip.Application.Organizations;

namespace Workslip.Api.Endpoints;

public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations").WithTags("organizations").RequireAuthorization(AuthPolicies.Admin);

        group.MapPost("/", async (CreateOrganizationRequest request, HttpContext httpContext, IOrganizationService service, CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.CreateAsync(request, cancellationToken);
            return result.Status switch
            {
                OrganizationServiceResultStatus.Success when result.Value is not null => Results.Created($"/api/organizations/{result.Value.Organization.Id}", result.Value),
                OrganizationServiceResultStatus.ValidationFailed => Results.ValidationProblem(ToProblem(result.Errors)),
                OrganizationServiceResultStatus.Conflict => Results.Conflict(new { error = result.ErrorCode, message = result.Message }),
                _ => Results.Problem("Unable to create organization.")
            };
        });

        return app;
    }

    private static Dictionary<string, string[]> ToProblem(IEnumerable<OrganizationValidationError> errors) =>
        errors.GroupBy(error => error.Field)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray());
}
