using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Workslip.Api.Configuration;
using Workslip.Application.Auth;

namespace Workslip.Api.Middleware;

public sealed class ShopifyTenantResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public ShopifyTenantResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentUserContext currentUser, IShopifyTenantResolver tenantResolver)
    {
        // Only process Shopify webhook endpoints
        if (context.Request.Path.StartsWithSegments("/api/payments/webhook/shopify"))
        {
            var shopDomain = context.Request.Headers["X-Shopify-Shop-Domain"].FirstOrDefault();

            if (!string.IsNullOrEmpty(shopDomain))
            {
                var tenantId = tenantResolver.ResolveTenantId(shopDomain);
                if (!string.IsNullOrEmpty(tenantId))
                {
                    context.Request.Headers["X-Workslip-Tenant-Id"] = tenantId;
                }
            }
        }

        await _next(context);
    }
}

public static class ShopifyTenantResolutionMiddlewareExtensions
{
    public static IApplicationBuilder UseShopifyTenantResolution(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ShopifyTenantResolutionMiddleware>();
    }
}