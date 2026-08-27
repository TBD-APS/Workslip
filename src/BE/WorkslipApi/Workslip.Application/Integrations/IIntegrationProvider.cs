namespace Workslip.Application.Integrations;

public interface IIntegrationProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    Task<bool> TestConnectionAsync(string tenantId);
}
