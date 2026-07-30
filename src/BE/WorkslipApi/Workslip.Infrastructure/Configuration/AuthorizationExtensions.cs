using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

public static class AuthorizationExtensions
{
    public static RouteGroupBuilder MapReadGroup(this IEndpointRouteBuilder app, string prefix, string? tag = null)
    {
        var path = prefix.TrimEnd('/');
        var group = app.MapGroup(path).RequireAuthorization(AuthPolicies.RequireReadAccess);
        if (tag is not null) group.WithTags(tag);
        return group;
    }

    public static RouteGroupBuilder MapUserGroup(this IEndpointRouteBuilder app, string prefix, string? tag = null)
    {
        var path = prefix.TrimEnd('/');
        var group = app.MapGroup(path).RequireAuthorization(AuthPolicies.RequireUser);
        if (tag is not null) group.WithTags(tag);
        return group;
    }

    public static RouteGroupBuilder MapAdminGroup(this IEndpointRouteBuilder app, string prefix, string? tag = null)
    {
        var path = prefix.TrimEnd('/');
        var group = app.MapGroup(path).RequireAuthorization(AuthPolicies.RequireAdmin);
        if (tag is not null) group.WithTags(tag);
        return group;
    }

    public static RouteGroupBuilder MapSuperAdminGroup(this IEndpointRouteBuilder app, string prefix, string? tag = null)
    {
        var path = prefix.TrimEnd('/');
        var group = app.MapGroup(path).RequireAuthorization(AuthPolicies.RequireSuperAdmin);
        if (tag is not null) group.WithTags(tag);
        return group;
    }

    public static (RouteGroupBuilder read, RouteGroupBuilder user) MapReadUserGroups(
        this IEndpointRouteBuilder app, string prefix, string? tag = null)
    {
        var path = prefix.TrimEnd('/');
        var read = app.MapGroup(path).RequireAuthorization(AuthPolicies.RequireReadAccess);
        var user = app.MapGroup(path).RequireAuthorization(AuthPolicies.RequireUser);
        if (tag is not null)
        {
            read.WithTags(tag);
            user.WithTags(tag);
        }
        return (read, user);
    }

    public static (RouteGroupBuilder read, RouteGroupBuilder admin) MapReadAdminGroups(
        this IEndpointRouteBuilder app, string prefix, string? tag = null)
    {
        var path = prefix.TrimEnd('/');
        var read = app.MapGroup(path).RequireAuthorization(AuthPolicies.RequireReadAccess);
        var admin = app.MapGroup(path).RequireAuthorization(AuthPolicies.RequireAdmin);
        if (tag is not null)
        {
            read.WithTags(tag);
            admin.WithTags(tag);
        }
        return (read, admin);
    }
}
