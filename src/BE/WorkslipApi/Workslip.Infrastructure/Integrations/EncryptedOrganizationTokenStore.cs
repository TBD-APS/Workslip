using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Integrations;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Integrations;

public sealed class EncryptedOrganizationTokenStore(
    SqlDbContext db,
    IDataProtectionProvider dataProtection) : IAccountingTokenStore
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector("Workslip.AccountingTokens.v1");

    public async Task<AccountingTokens> GetAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var row = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == organizationId)
            .Select(o => new { o.EconomicsAgreementGrantTokenEncrypted, o.EconomicsAppSecretTokenEncrypted })
            .FirstOrDefaultAsync(cancellationToken);

        if (row == null) return new AccountingTokens(null, null);

        return new AccountingTokens(
            TryUnprotect(row.EconomicsAgreementGrantTokenEncrypted),
            TryUnprotect(row.EconomicsAppSecretTokenEncrypted));
    }

    public async Task SetAsync(Guid organizationId, string? agreementGrantToken, string? appSecretToken, CancellationToken cancellationToken)
    {
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);
        if (org == null) throw new InvalidOperationException($"Organization {organizationId} not found.");

        org.EconomicsAgreementGrantTokenEncrypted = Protect(agreementGrantToken);
        org.EconomicsAppSecretTokenEncrypted = Protect(appSecretToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private string? Protect(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : _protector.Protect(value.Trim());

    private string? TryUnprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue)) return null;
        try { return _protector.Unprotect(protectedValue); }
        catch { return null; }
    }
}
