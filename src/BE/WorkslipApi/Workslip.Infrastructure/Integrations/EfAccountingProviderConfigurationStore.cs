using Microsoft.EntityFrameworkCore;
using Workslip.Application.Integrations;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Integrations;

public sealed class EfAccountingProviderConfigurationStore(
    SqlDbContext dbContext,
    IDatabaseRetryPolicy retryPolicy) : IAccountingProviderConfigurationStore
{
    public Task<bool> SetProviderAsync(
        Guid organizationId,
        string? providerId,
        CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            "accounting-provider-settings.update",
            token => SetProviderCoreAsync(organizationId, providerId, token),
            cancellationToken);

    private async Task<bool> SetProviderCoreAsync(
        Guid organizationId,
        string? providerId,
        CancellationToken cancellationToken)
    {
        var updatedAt = DateTimeOffset.UtcNow;
        var affectedRows = await dbContext.Organizations
            .Where(organization => organization.Id == organizationId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(organization => organization.AccountingProviderId, providerId)
                    .SetProperty(organization => organization.UpdatedAt, updatedAt),
                cancellationToken);

        return affectedRows == 1;
    }
}
