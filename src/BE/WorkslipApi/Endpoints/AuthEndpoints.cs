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
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("Workslip.Api.Endpoints.Auth");
            var user = await repository.GetCurrentUserAsync(userId, cancellationToken);
            if (user is null)
            {
                logger.LogWarning("Current user lookup returned not found. UserId: {UserId}.", userId);
                return Results.NotFound();
            }

            logger.LogInformation("Current user fetched. UserId: {UserId}. OrganizationId: {OrganizationId}. Role: {Role}.",
                user.Id,
                user.Organization.Id,
                user.Role);

            return Results.Ok(user);
        });

        return app;
    }
}
