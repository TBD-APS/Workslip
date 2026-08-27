using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Workslip.Application.Organizations;

namespace Workslip.Application.Integrations;

public interface IIntegrationEngine
{
    Task<IAccountingProvider> GetAccountingProviderAsync(string tenantId);
    IEnumerable<IIntegrationProvider> GetAvailableProviders();
}

public class IntegrationEngine : IIntegrationEngine
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOrganizationRepository _organizationRepository;

    public IntegrationEngine(IServiceProvider serviceProvider, IOrganizationRepository organizationRepository)
    {
        _serviceProvider = serviceProvider;
        _organizationRepository = organizationRepository;
    }

    public IEnumerable<IIntegrationProvider> GetAvailableProviders()
    {
        return _serviceProvider.GetServices<IIntegrationProvider>();
    }

    public async Task<IAccountingProvider> GetAccountingProviderAsync(string tenantId)
    {
        if (!Guid.TryParse(tenantId, out var organizationId))
        {
            throw new ArgumentException("Invalid tenantId format. Expected a GUID.", nameof(tenantId));
        }

        var organization = await _organizationRepository.GetByIdAsync(organizationId, default);
        if (organization is null)
        {
            throw new NotSupportedException($"Organization with ID {tenantId} not found.");
        }

        var providerId = organization.AccountingProviderId;
        if (string.IsNullOrWhiteSpace(providerId))
        {
            // Default to mock if not configured, or throw. Let's default to mock for now.
            providerId = "mock";
        }

        var providers = _serviceProvider.GetServices<IAccountingProvider>();
        return providers.FirstOrDefault(p => p.ProviderId == providerId) 
               ?? throw new NotSupportedException($"Accounting provider '{providerId}' is not supported or not registered.");
    }
}
