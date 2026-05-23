using Workslip.Application.Users;

namespace Workslip.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("users")
            .RequireAuthorization(AuthPolicies.RequireAdmin);

        group.MapPost("/", async (CreateUserRequest request, UserService service, CancellationToken cancellationToken) =>
        {
            var (success, user, errors) = await service.CreateAsync(request, cancellationToken);
            if (!success)
                return Results.BadRequest(new { errors });

            return Results.Created($"/api/users/{user?.Id}", user);
        });

        group.MapGet("/{id}", async (Guid id, UserService service, CancellationToken cancellationToken) =>
        {
            var (success, user, errors) = await service.GetAsync(id, cancellationToken);
            if (!success)
                return Results.NotFound(new { errors });

            return Results.Ok(user);
        });

        group.MapGet("/organization/{organizationId}", async (Guid organizationId, UserService service, CancellationToken cancellationToken) =>
        {
            var (success, users, errors) = await service.GetByOrganizationAsync(organizationId, cancellationToken);
            if (!success)
                return Results.BadRequest(new { errors });

            return Results.Ok(users);
        });

        group.MapPatch("/{id}", async (Guid id, UpdateUserRequest request, UserService service, CancellationToken cancellationToken) =>
        {
            var (success, user, errors) = await service.UpdateAsync(id, request, cancellationToken);
            if (!success)
                return Results.BadRequest(new { errors });

            return Results.Ok(user);
        });

        group.MapDelete("/{id}", async (Guid id, UserService service, CancellationToken cancellationToken) =>
        {
            var (success, errors) = await service.DeleteAsync(id, cancellationToken);
            if (!success)
                return Results.NotFound(new { errors });

            return Results.NoContent();
        });

        return app;
    }
}
