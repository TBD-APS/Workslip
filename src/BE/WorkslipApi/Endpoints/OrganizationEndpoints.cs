using Workslip.Api.Helpers;
using Workslip.Api.ViewModels;
using Workslip.Application.Auth;
using Workslip.Application.Organizations;

namespace Workslip.Api.Endpoints;

public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapSuperAdminGroup("/api/organizations", "organizations");

        group.MapGet("/", async (
            IOrganizationService service,
            CancellationToken cancellationToken) =>
        {
            var result = await service.ListAsync(cancellationToken);
            return ResultExtensions.ToHttpResult(
                result,
                organizations => organizations.Select(OrganizationViewModelBuilder.ToOrganization).ToList());
        }).Produces<IReadOnlyList<OrganizationViewModel>>();

        group.MapPost("/", async (
            CreateOrganizationRequest request,
            IOrganizationService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.CreateAsync(request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, OrganizationViewModelBuilder.ToOnboarding);
        }).Produces<OrganizationOnboardingViewModel>();

        group.MapPost("/{organizationId:guid}/session", async (
            Guid organizationId,
            IOrganizationSessionService service,
            IConfiguration configuration,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.CreateAsync(organizationId, cancellationToken);
            return ResultExtensions.ToHttpResult(
                result,
                session => JwtHelper.GenerateOrganizationSessionToken(
                    session.User,
                    session.HomeOrganizationId,
                    configuration));
        }).Produces<AuthTokenResponse>();

        group.MapPut("/{organizationId:guid}/admin", async (
            Guid organizationId,
            UpsertOrganizationAdminRequest request,
            IOrganizationService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            HttpCacheHeaders.SetNoStore(httpContext);
            var result = await service.UpsertAdminAsync(organizationId, request, cancellationToken);
            return ResultExtensions.ToHttpResult(result, OrganizationViewModelBuilder.ToOrganizationUser);
        }).Produces<OrganizationUserViewModel>();

        return app;
    }
}
