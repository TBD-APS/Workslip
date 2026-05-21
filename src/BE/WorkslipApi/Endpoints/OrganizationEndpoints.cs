using Workslip.Application.Organizations;

namespace Workslip.Api.Endpoints;

public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/organizations").WithTags("organizations");

        group.MapPost("/", async (
            CreateOrganizationRequest request,
            IOrganizationRepository repository,
            CancellationToken cancellationToken) =>
        {
            var errors = OrganizationRequestValidator.ValidateCreate(request);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors
                    .GroupBy(error => error.Field)
                    .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray()));
            }

            var normalizedCvr = OrganizationRequestValidator.NormalizeCvr(request.Cvr);
            if (await repository.CvrExistsAsync(normalizedCvr, cancellationToken))
            {
                return Results.Conflict(new { error = "organization_cvr_exists", message = "An organization with this CVR already exists." });
            }

            var created = await repository.CreateAsync(request, normalizedCvr, cancellationToken);
            return created is null
                ? Results.Conflict(new { error = "organization_cvr_exists", message = "An organization with this CVR already exists." })
                : Results.Created($"/api/organizations/{created.Organization.Id}", created);
        });

        return app;
    }
}
