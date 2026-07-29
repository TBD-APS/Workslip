using Workslip.Api.Helpers;
using Workslip.Api.ViewModels;
using Workslip.Application.Organizations;

namespace Workslip.Api.Endpoints;

public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapSuperAdminGroup("/api/organizations", "organizations");

        group.MapPost("/", async (
            CreateOrganizationRequest request,
            IOrganizationService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, OrganizationViewModelBuilder.ToOnboarding);
        }).Produces<OrganizationOnboardingViewModel>();

        group.MapPut("/{organizationId:guid}/admin", async (
            Guid organizationId,
            UpsertOrganizationAdminRequest request,
            IOrganizationService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpsertAdminAsync(organizationId, request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, OrganizationViewModelBuilder.ToOrganizationUser);
        }).Produces<OrganizationUserViewModel>();

        return app;
    }
}
