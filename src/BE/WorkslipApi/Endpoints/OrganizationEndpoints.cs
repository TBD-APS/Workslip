using Workslip.Api.Helpers;
using Workslip.Api.ViewModels;
using Workslip.Application.Organizations;

namespace Workslip.Api.Endpoints;

public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapAdminGroup("/api/organizations", "organizations");

        group.MapPost("/", async (CreateOrganizationRequest request, IOrganizationService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, OrganizationViewModelBuilder.ToOnboarding);
        });

        return app;
    }
}
