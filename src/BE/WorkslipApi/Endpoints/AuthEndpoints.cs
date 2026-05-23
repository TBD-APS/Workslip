using Microsoft.AspNetCore.Http.HttpResults;
using Workslip.Application.Organizations;

namespace Workslip.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("auth").RequireAuthorization(AuthPolicies.RequireSuperAdmin);

        group.MapPost("admin/user", async () =>
        {
            return Results.Ok();
        });



        return app;
    }
}
