using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Workslip.Application.Integrations;
using Workslip.Infrastructure.Repositories;
using Workslip.Api.Middleware;

namespace Workslip.Api.Configuration;

public static class ShopifyTenantMappingExtensions
{
    public static IServiceCollection AddShopifyTenantMapping(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ShopifyTenantMappingOptions>(
            configuration.GetSection(ShopifyTenantMappingOptions.SectionName));

        services.AddScoped<IShopifyTenantMappingRepository, EfShopifyTenantMappingRepository>();
        services.AddSingleton<IShopifyTenantResolver, DatabaseShopifyTenantResolver>();

        return services;
    }
}

public class ShopifyTenantMappingOptions
{
    public const string SectionName = "Shopify:TenantMappings";
    public Dictionary<string, string> Mappings { get; set; } = new();
    public string? DefaultTenantId { get; set; }
}

public interface IShopifyTenantResolver
{
    string? ResolveTenantId(string shopDomain);
}