using Workslip.Application.Organizations;

namespace Workslip.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("auth");

        group.MapGet("/me", async (Guid userId, IAuthService service, CancellationToken cancellationToken) =>
        {
            var result = await service.GetCurrentUserAsync(userId, cancellationToken);
            return result.Status switch
            {
                OrganizationServiceResultStatus.Success when result.Value is not null => Results.Ok(result.Value),
                OrganizationServiceResultStatus.NotFound => Results.NotFound(),
                _ => Results.Problem("Unable to get current user.")
            };
        });

        return app;
    }
}
