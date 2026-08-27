using Ardalis.Result;
using Workslip.Application.Auth;
using Workslip.Application.Organizations;

namespace Workslip.Application.Integrations;

public sealed record AccountingProviderOptionResponse(string Id, string DisplayName);

public sealed record AccountingProviderSettingsResponse(
    string? ProviderId,
    IReadOnlyList<AccountingProviderOptionResponse> Providers);

public sealed record UpdateAccountingProviderRequest(string? ProviderId);

public interface IAccountingProviderConfigurationStore
{
    Task<bool> SetProviderAsync(
        Guid organizationId,
        string? providerId,
        CancellationToken cancellationToken);
}

public interface IAccountingProviderSettingsService
{
    Task<Result<AccountingProviderSettingsResponse>> GetAsync(CancellationToken cancellationToken);
    Task<Result> UpdateAsync(UpdateAccountingProviderRequest request, CancellationToken cancellationToken);
}

public sealed class AccountingProviderSettingsService(
    IOrganizationRepository organizations,
    IAccountingProviderConfigurationStore configurationStore,
    IEnumerable<IAccountingProvider> accountingProviders,
    ICurrentUserContext currentUser) : IAccountingProviderSettingsService
{
    private const string DevelopmentMockProviderId = "mock";

    public async Task<Result<AccountingProviderSettingsResponse>> GetAsync(
        CancellationToken cancellationToken)
    {
        if (currentUser.OrganizationId is not Guid organizationId)
            return Result<AccountingProviderSettingsResponse>.Unauthorized();

        var organization = await organizations.GetByIdAsync(organizationId, cancellationToken);
        if (organization is null)
            return Result<AccountingProviderSettingsResponse>.NotFound();

        var providers = GetSelectableProviders()
            .Select(provider => new AccountingProviderOptionResponse(
                provider.ProviderId,
                provider.DisplayName))
            .ToArray();

        return Result<AccountingProviderSettingsResponse>.Success(
            new AccountingProviderSettingsResponse(
                organization.AccountingProviderId,
                providers));
    }

    public async Task<Result> UpdateAsync(
        UpdateAccountingProviderRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUser.OrganizationId is not Guid organizationId)
            return Result.Unauthorized();

        var organization = await organizations.GetByIdAsync(organizationId, cancellationToken);
        if (organization is null)
            return Result.NotFound();

        var requestedProviderId = string.IsNullOrWhiteSpace(request.ProviderId)
            ? null
            : request.ProviderId.Trim();

        if (requestedProviderId is not null)
        {
            var provider = GetSelectableProviders().FirstOrDefault(candidate =>
                string.Equals(
                    candidate.ProviderId,
                    requestedProviderId,
                    StringComparison.OrdinalIgnoreCase));

            if (provider is null)
            {
                return Result.Invalid(new ValidationError
                {
                    Identifier = nameof(UpdateAccountingProviderRequest.ProviderId),
                    ErrorMessage = "Det valgte regnskabssystem understøttes ikke."
                });
            }

            requestedProviderId = provider.ProviderId;
        }

        var updated = await configurationStore.SetProviderAsync(
            organizationId,
            requestedProviderId,
            cancellationToken);

        return updated ? Result.NoContent() : Result.NotFound();
    }

    private IReadOnlyList<IAccountingProvider> GetSelectableProviders() =>
        accountingProviders
            .Where(provider =>
                !string.IsNullOrWhiteSpace(provider.ProviderId) &&
                !string.Equals(
                    provider.ProviderId,
                    DevelopmentMockProviderId,
                    StringComparison.OrdinalIgnoreCase))
            .GroupBy(provider => provider.ProviderId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(provider => provider.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
}
