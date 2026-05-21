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
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("Workslip.Api.Endpoints.Organizations");
            var errors = OrganizationRequestValidator.ValidateCreate(request);
            if (errors.Count > 0)
            {
                logger.LogWarning("Organization create validation failed. Fields: {ValidationFields}",
                    string.Join(",", errors.Select(error => error.Field).Distinct()));

                return Results.ValidationProblem(errors
                    .GroupBy(error => error.Field)
                    .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray()));
            }

            var normalizedCvr = OrganizationRequestValidator.NormalizeCvr(request.Cvr);
            if (await repository.CvrExistsAsync(normalizedCvr, cancellationToken))
            {
                logger.LogWarning("Organization create conflict. Reason: {Reason}. Cvr: {Cvr}.",
                    "organization_cvr_exists",
                    normalizedCvr);

                return Results.Conflict(new { error = "organization_cvr_exists", message = "An organization with this CVR already exists." });
            }

            var created = await repository.CreateAsync(request, normalizedCvr, cancellationToken);
            if (created is null)
            {
                logger.LogWarning("Organization create conflict after insert attempt. Reason: {Reason}. Cvr: {Cvr}.",
                    "organization_cvr_exists",
                    normalizedCvr);

                return Results.Conflict(new { error = "organization_cvr_exists", message = "An organization with this CVR already exists." });
            }

            logger.LogInformation("Organization created. OrganizationId: {OrganizationId}. UserId: {UserId}. Cvr: {Cvr}.",
                created.Organization.Id,
                created.User.Id,
                normalizedCvr);

            return Results.Created($"/api/organizations/{created.Organization.Id}", created);
        });

        return app;
    }
}
