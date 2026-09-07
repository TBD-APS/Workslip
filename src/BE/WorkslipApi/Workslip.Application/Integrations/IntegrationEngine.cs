using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Workslip.Application.Integrations;

public interface IIntegrationEngine
{
    Task<IAccountingProvider> GetAccountingProviderAsync(string tenantId);
    Task<IAccountingOperationsProvider> GetAccountingOperationsProviderAsync(string tenantId);
    IEnumerable<IIntegrationProvider> GetAvailableProviders();
}

public class IntegrationEngine : IIntegrationEngine
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly IEconomicConnectionStore? _economicConnections;

    public IntegrationEngine(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        IEconomicConnectionStore? economicConnections = null)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _economicConnections = economicConnections;
    }

    public IEnumerable<IIntegrationProvider> GetAvailableProviders() =>
        _serviceProvider.GetServices<IIntegrationProvider>();

    public async Task<IAccountingProvider> GetAccountingProviderAsync(string tenantId)
    {
        var providerId = await ResolveProviderIdAsync(tenantId);
        var provider = _serviceProvider
            .GetServices<IAccountingProvider>()
            .FirstOrDefault(candidate => string.Equals(candidate.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));

        return provider
            ?? throw new NotSupportedException($"Accounting provider '{providerId}' is not registered.");
    }

    public async Task<IAccountingOperationsProvider> GetAccountingOperationsProviderAsync(string tenantId)
    {
        var provider = await GetAccountingProviderAsync(tenantId);
        return provider as IAccountingOperationsProvider
            ?? throw new NotSupportedException($"Accounting provider '{provider.ProviderId}' does not support operational synchronization.");
    }

    private async Task<string> ResolveProviderIdAsync(string tenantId)
    {
        var explicitProvider = _configuration[$"Integrations:Accounting:Organizations:{tenantId}:Provider"];
        if (!string.IsNullOrWhiteSpace(explicitProvider))
            return explicitProvider.Trim();

        if (_economicConnections is not null && Guid.TryParse(tenantId, out var organizationId) &&
            await _economicConnections.HasConnectionAsync(organizationId, CancellationToken.None))
            return "economics";

        var economicGrant = _configuration[$"Integrations:Economic:Agreements:{tenantId}:GrantToken"];
        if (!string.IsNullOrWhiteSpace(economicGrant))
            return "economics";

        // Preserve existing local/dev behavior until an organization explicitly connects accounting.
        return "mock";
    }
}
