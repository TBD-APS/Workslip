using Workslip.Application.Organizations;

namespace Workslip.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("auth");

        group.MapGet("/me", async (
            Guid userId,
            IOrganizationRepository repository,
            CancellationToken cancellationToken) =>
        {
            var user = await repository.GetCurrentUserAsync(userId, cancellationToken);
            return user is null ? Results.NotFound() : Results.Ok(user);
        });

        return app;
    }
}
