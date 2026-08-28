using Workslip.Api.Configuration;
using Workslip.Application.Integrations;

namespace Workslip.Api.Middleware;

public sealed class DatabaseShopifyTenantResolver : IShopifyTenantResolver
{
    private readonly IServiceProvider _serviceProvider;

    public DatabaseShopifyTenantResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public string? ResolveTenantId(string shopDomain)
    {
        if (string.IsNullOrEmpty(shopDomain))
        {
            return null;
        }

        using var scope = _serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IShopifyTenantMappingRepository>();

        var task = repository.GetByShopDomainAsync(shopDomain, CancellationToken.None);
        task.Wait();

        var mapping = task.Result;
        return mapping?.OrganizationId.ToString();
    }
}