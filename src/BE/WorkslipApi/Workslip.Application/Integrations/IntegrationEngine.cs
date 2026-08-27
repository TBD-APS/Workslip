using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Workslip.Application.Integrations;

public interface IIntegrationEngine
{
    Task<IAccountingProvider> GetAccountingProviderAsync(string tenantId);
    IEnumerable<IIntegrationProvider> GetAvailableProviders();
}

public class IntegrationEngine : IIntegrationEngine
{
    private readonly IServiceProvider _serviceProvider;

    public IntegrationEngine(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IEnumerable<IIntegrationProvider> GetAvailableProviders()
    {
        return _serviceProvider.GetServices<IIntegrationProvider>();
    }

    public async Task<IAccountingProvider> GetAccountingProviderAsync(string tenantId)
    {
        // In a real scenario, we would lookup the tenant's configured provider in the DB.
        // For now, we default to the Mock provider for development.
        var providers = _serviceProvider.GetServices<IAccountingProvider>();
        return providers.FirstOrDefault(p => p.ProviderId == "mock") 
               ?? throw new NotSupportedException("No accounting provider configured for this tenant.");
    }
}
