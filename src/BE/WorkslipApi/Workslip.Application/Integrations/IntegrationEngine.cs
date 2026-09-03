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

    public IntegrationEngine(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    public IEnumerable<IIntegrationProvider> GetAvailableProviders() =>
        _serviceProvider.GetServices<IIntegrationProvider>();

    public Task<IAccountingProvider> GetAccountingProviderAsync(string tenantId)
    {
        var providerId = ResolveProviderId(tenantId);
        var provider = _serviceProvider
            .GetServices<IAccountingProvider>()
            .FirstOrDefault(candidate => string.Equals(candidate.ProviderId, providerId, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(provider
            ?? throw new NotSupportedException($"Accounting provider '{providerId}' is not registered."));
    }

    public async Task<IAccountingOperationsProvider> GetAccountingOperationsProviderAsync(string tenantId)
    {
        var provider = await GetAccountingProviderAsync(tenantId);
        return provider as IAccountingOperationsProvider
            ?? throw new NotSupportedException($"Accounting provider '{provider.ProviderId}' does not support operational synchronization.");
    }

    private string ResolveProviderId(string tenantId)
    {
        var explicitProvider = _configuration[$"Integrations:Accounting:Organizations:{tenantId}:Provider"];
        if (!string.IsNullOrWhiteSpace(explicitProvider))
            return explicitProvider.Trim();

        var economicGrant = _configuration[$"Integrations:Economic:Agreements:{tenantId}:GrantToken"];
        if (!string.IsNullOrWhiteSpace(economicGrant))
            return "economics";

        // Preserve existing local/dev behavior until an organization explicitly connects accounting.
        return "mock";
    }
}
