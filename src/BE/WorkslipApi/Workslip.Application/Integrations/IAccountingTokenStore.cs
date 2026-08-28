namespace Workslip.Application.Integrations;

public sealed record AccountingTokens(string? AgreementGrantToken, string? AppSecretToken);

public interface IAccountingTokenStore
{
    Task<AccountingTokens> GetAsync(Guid organizationId, CancellationToken cancellationToken);
    Task SetAsync(Guid organizationId, string? agreementGrantToken, string? appSecretToken, CancellationToken cancellationToken);
}
