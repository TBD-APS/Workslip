using System.Threading.Tasks;
using Workslip.Domain.Models;

namespace Workslip.Application.Integrations;

public interface IShopifyTenantMappingRepository
{
    Task<ShopifyTenantMappingRow?> GetByShopDomainAsync(string shopDomain, CancellationToken cancellationToken);
    Task<ShopifyTenantMappingRow?> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<ShopifyTenantMappingRow> CreateAsync(ShopifyTenantMappingRow mapping, CancellationToken cancellationToken);
    Task<ShopifyTenantMappingRow> UpdateAsync(ShopifyTenantMappingRow mapping, CancellationToken cancellationToken);
}