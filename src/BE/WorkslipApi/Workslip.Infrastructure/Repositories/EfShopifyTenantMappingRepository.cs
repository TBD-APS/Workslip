using Microsoft.EntityFrameworkCore;
using Workslip.Application.Integrations;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfShopifyTenantMappingRepository : IShopifyTenantMappingRepository
{
    private readonly SqlDbContext _dbContext;
    private readonly IDatabaseRetryPolicy _retryPolicy;

    public EfShopifyTenantMappingRepository(
        SqlDbContext dbContext,
        IDatabaseRetryPolicy retryPolicy)
    {
        _dbContext = dbContext;
        _retryPolicy = retryPolicy;
    }

    public Task<ShopifyTenantMappingRow?> GetByShopDomainAsync(string shopDomain, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "shopify_tenant_mappings.get_by_shop_domain",
            token => GetByShopDomainAsyncCoreAsync(shopDomain, token),
            cancellationToken);

    public Task<ShopifyTenantMappingRow?> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "shopify_tenant_mappings.get_by_organization_id",
            token => GetByOrganizationIdAsyncCoreAsync(organizationId, token),
            cancellationToken);

    public Task<ShopifyTenantMappingRow> CreateAsync(ShopifyTenantMappingRow mapping, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "shopify_tenant_mappings.create",
            token => CreateAsyncCoreAsync(mapping, token),
            cancellationToken);

    public Task<ShopifyTenantMappingRow> UpdateAsync(ShopifyTenantMappingRow mapping, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "shopify_tenant_mappings.update",
            token => UpdateAsyncCoreAsync(mapping, token),
            cancellationToken);

    private async Task<ShopifyTenantMappingRow?> GetByShopDomainAsyncCoreAsync(string shopDomain, CancellationToken cancellationToken)
    {
        var normalizedDomain = NormalizeShopDomain(shopDomain);
        return await _dbContext.ShopifyTenantMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.ShopDomain == normalizedDomain && m.IsActive, cancellationToken);
    }

    private async Task<ShopifyTenantMappingRow?> GetByOrganizationIdAsyncCoreAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        return await _dbContext.ShopifyTenantMappings
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId && m.IsActive, cancellationToken);
    }

    private async Task<ShopifyTenantMappingRow> CreateAsyncCoreAsync(ShopifyTenantMappingRow mapping, CancellationToken cancellationToken)
    {
        _dbContext.ShopifyTenantMappings.Add(mapping);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return mapping;
    }

    private async Task<ShopifyTenantMappingRow> UpdateAsyncCoreAsync(ShopifyTenantMappingRow mapping, CancellationToken cancellationToken)
    {
        mapping.UpdatedAt = DateTimeOffset.UtcNow;
        _dbContext.ShopifyTenantMappings.Update(mapping);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return mapping;
    }

    private static string NormalizeShopDomain(string shopDomain)
    {
        return shopDomain.Trim().ToLowerInvariant().Replace(".myshopify.com", "");
    }
}