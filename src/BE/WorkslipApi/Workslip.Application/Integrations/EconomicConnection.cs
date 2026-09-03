using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Workslip.Application.Auth;

namespace Workslip.Application.Integrations;

public sealed record EconomicConnectionMetadata(
    string? AgreementNumber,
    string? CompanyName,
    DateTimeOffset ConnectedAt,
    DateTimeOffset UpdatedAt);

public sealed record EconomicConnectionStatusResponse(
    bool Available,
    bool Connected,
    string ProviderId,
    string ProviderDisplayName,
    string? AgreementNumber,
    string? CompanyName,
    DateTimeOffset? ConnectedAt);

public sealed record EconomicConnectionStartResponse(string InstallationUrl, string CorrelationToken);

public sealed record EconomicAgreementIdentity(string? AgreementNumber, string? CompanyName);

public interface IEconomicConnectionStore
{
    Task<bool> HasConnectionAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<string?> GetAgreementGrantTokenAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<EconomicConnectionMetadata?> GetConnectionMetadataAsync(Guid organizationId, CancellationToken cancellationToken);
    Task SaveConnectionAsync(
        Guid organizationId,
        string agreementGrantToken,
        EconomicAgreementIdentity identity,
        CancellationToken cancellationToken);
    Task DeleteConnectionAsync(Guid organizationId, CancellationToken cancellationToken);
    Task SaveConnectAttemptAsync(
        Guid organizationId,
        string correlationHash,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);
    Task<Guid?> ConsumeConnectAttemptAsync(
        string correlationHash,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface IEconomicConnectionVerifier
{
    Task<EconomicAgreementIdentity> VerifyGrantTokenAsync(
        string agreementGrantToken,
        CancellationToken cancellationToken);
}

public interface IEconomicConnectionService
{
    Task<EconomicConnectionStatusResponse> GetStatusAsync(CancellationToken cancellationToken);
    Task<EconomicConnectionStartResponse> StartAsync(CancellationToken cancellationToken);
    Task CompleteAsync(
        string correlationToken,
        string agreementGrantToken,
        CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
}

public sealed class EconomicConnectionService(
    IEconomicConnectionStore store,
    IEconomicConnectionVerifier verifier,
    ICurrentUserContext currentUser,
    IConfiguration configuration) : IEconomicConnectionService
{
    private static readonly TimeSpan ConnectAttemptLifetime = TimeSpan.FromMinutes(10);

    public async Task<EconomicConnectionStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        var organizationId = RequireOrganization();
        var metadata = await store.GetConnectionMetadataAsync(organizationId, cancellationToken);
        var installationUrl = configuration["Integrations:Economic:InstallationUrl"];
        var appSecret = configuration["Integrations:Economic:AppSecretToken"];

        return new EconomicConnectionStatusResponse(
            Available: IsSafeInstallationUrl(installationUrl) && !string.IsNullOrWhiteSpace(appSecret),
            Connected: metadata is not null,
            ProviderId: "economics",
            ProviderDisplayName: "e-conomic",
            AgreementNumber: metadata?.AgreementNumber,
            CompanyName: metadata?.CompanyName,
            ConnectedAt: metadata?.ConnectedAt);
    }

    public async Task<EconomicConnectionStartResponse> StartAsync(CancellationToken cancellationToken)
    {
        var organizationId = RequireOrganization();
        var installationUrl = configuration["Integrations:Economic:InstallationUrl"];
        var appSecret = configuration["Integrations:Economic:AppSecretToken"];

        if (!IsSafeInstallationUrl(installationUrl))
            throw new InvalidOperationException("e-conomic installation URL is not configured with a valid HTTPS URL.");
        if (string.IsNullOrWhiteSpace(appSecret))
            throw new InvalidOperationException("e-conomic app secret is not configured.");

        var correlationToken = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var correlationHash = HashCorrelation(correlationToken);
        await store.SaveConnectAttemptAsync(
            organizationId,
            correlationHash,
            DateTimeOffset.UtcNow.Add(ConnectAttemptLifetime),
            cancellationToken);

        return new EconomicConnectionStartResponse(AddLocale(installationUrl!), correlationToken);
    }

    public async Task CompleteAsync(
        string correlationToken,
        string agreementGrantToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(correlationToken) || correlationToken.Length > 256)
            throw new InvalidOperationException("The e-conomic connection attempt is missing or invalid.");
        if (string.IsNullOrWhiteSpace(agreementGrantToken) || agreementGrantToken.Length > 4096)
            throw new InvalidOperationException("e-conomic did not return a valid agreement token.");

        var correlationHash = HashCorrelation(correlationToken);
        var organizationId = await store.ConsumeConnectAttemptAsync(
            correlationHash,
            DateTimeOffset.UtcNow,
            cancellationToken);
        if (organizationId is null)
            throw new InvalidOperationException("The e-conomic connection attempt has expired or was already used.");

        var identity = await verifier.VerifyGrantTokenAsync(agreementGrantToken.Trim(), cancellationToken);
        await store.SaveConnectionAsync(
            organizationId.Value,
            agreementGrantToken.Trim(),
            identity,
            cancellationToken);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken) =>
        store.DeleteConnectionAsync(RequireOrganization(), cancellationToken);

    private Guid RequireOrganization() => currentUser.OrganizationId
        ?? throw new UnauthorizedAccessException("Missing organization context.");

    private static string HashCorrelation(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool IsSafeInstallationUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(uri.Host);

    private static string AddLocale(string installationUrl)
    {
        var separator = installationUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return installationUrl.Contains("locale=", StringComparison.OrdinalIgnoreCase)
            ? installationUrl
            : $"{installationUrl}{separator}locale=da-DK";
    }
}
