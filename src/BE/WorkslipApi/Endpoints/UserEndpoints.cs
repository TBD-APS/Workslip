using Workslip.Application.Users;

namespace Workslip.Api.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("users")
            .RequireAuthorization(AuthPolicies.Admin);

        group.MapPost("/", CreateUser)
            .WithName("CreateUser")
            .WithOpenApi();

        group.MapGet("/{id}", GetUser)
            .WithName("GetUser")
            .WithOpenApi();

        group.MapGet("/organization/{organizationId}", GetOrganizationUsers)
            .WithName("GetOrganizationUsers")
            .WithOpenApi();

        group.MapPatch("/{id}", UpdateUser)
            .WithName("UpdateUser")
            .WithOpenApi();

        group.MapDelete("/{id}", DeleteUser)
            .WithName("DeleteUser")
            .WithOpenApi();

        return app;
    }

    private static async Task<IResult> CreateUser(
        CreateUserRequest request,
        UserService service,
        CancellationToken cancellationToken)
    {
        var (success, user, error) = await service.CreateAsync(request, cancellationToken);

        if (!success)
            return Results.BadRequest(new { error });

        return Results.Created($"/api/users/{user?.Id}", user);
    }

    private static async Task<IResult> GetUser(
        Guid id,
        UserService service,
        CancellationToken cancellationToken)
    {
        var (success, user, error) = await service.GetAsync(id, cancellationToken);

        if (!success)
            return Results.NotFound(new { error });

        return Results.Ok(user);
    }

    private static async Task<IResult> GetOrganizationUsers(
        Guid organizationId,
        UserService service,
        CancellationToken cancellationToken)
    {
        var (success, users, error) = await service.GetByOrganizationAsync(organizationId, cancellationToken);

        if (!success)
            return Results.BadRequest(new { error });

        return Results.Ok(users);
    }

    private static async Task<IResult> UpdateUser(
        Guid id,
        UpdateUserRequest request,
        UserService service,
        CancellationToken cancellationToken)
    {
        var (success, user, error) = await service.UpdateAsync(id, request, cancellationToken);

        if (!success)
            return Results.BadRequest(new { error });

        return Results.Ok(user);
    }

    private static async Task<IResult> DeleteUser(
        Guid id,
        UserService service,
        CancellationToken cancellationToken)
    {
        var (success, error) = await service.DeleteAsync(id, cancellationToken);

        if (!success)
            return Results.NotFound(new { error });

        return Results.NoContent();
    }
}
