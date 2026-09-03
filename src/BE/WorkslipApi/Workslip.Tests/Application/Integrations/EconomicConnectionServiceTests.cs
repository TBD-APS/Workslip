using Microsoft.Extensions.Configuration;
using Workslip.Application.Auth;
using Workslip.Application.Integrations;
using Xunit;

namespace Workslip.Tests.Application.Integrations;

public sealed class EconomicConnectionServiceTests
{
    private static IConfiguration Configuration(string installationUrl = "https://secure.e-conomic.com/secure/api1/requestaccess.aspx?appPublicToken=test") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:Economic:AppSecretToken"] = "app-secret",
                ["Integrations:Economic:InstallationUrl"] = installationUrl,
            })
            .Build();

    [Fact]
    public async Task StartAsync_CreatesExpiringCorrelation_AndAddsDanishLocale()
    {
        var organizationId = Guid.NewGuid();
        var store = new FakeStore();
        var service = new EconomicConnectionService(
            store,
            new FakeVerifier(),
            new MutableCurrentUserContext { OrganizationId = organizationId },
            Configuration());

        var result = await service.StartAsync(CancellationToken.None);

        Assert.Contains("locale=da-DK", result.InstallationUrl, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(result.CorrelationToken);
        Assert.Single(store.Attempts);
        var attempt = store.Attempts.Single();
        Assert.Equal(organizationId, attempt.Value.OrganizationId);
        Assert.InRange(attempt.Value.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(9), DateTimeOffset.UtcNow.AddMinutes(11));
        Assert.DoesNotContain(result.CorrelationToken, attempt.Key, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_UsesOneTimeCorrelationTenant_NotCurrentRequestTenant()
    {
        var organizationA = Guid.NewGuid();
        var organizationB = Guid.NewGuid();
        var currentUser = new MutableCurrentUserContext { OrganizationId = organizationA };
        var store = new FakeStore();
        var verifier = new FakeVerifier(new EconomicAgreementIdentity("123456", "Niels VVS"));
        var service = new EconomicConnectionService(store, verifier, currentUser, Configuration());
        var start = await service.StartAsync(CancellationToken.None);

        currentUser.OrganizationId = organizationB;
        await service.CompleteAsync(start.CorrelationToken, "grant-token", CancellationToken.None);

        Assert.True(store.Connections.TryGetValue(organizationA, out var saved));
        Assert.False(store.Connections.ContainsKey(organizationB));
        Assert.Equal("grant-token", saved.Token);
        Assert.Equal("123456", saved.Identity.AgreementNumber);
        Assert.Equal(1, verifier.Calls);
    }

    [Fact]
    public async Task CompleteAsync_RejectsReplayedCorrelation()
    {
        var store = new FakeStore();
        var verifier = new FakeVerifier();
        var service = new EconomicConnectionService(
            store,
            verifier,
            new MutableCurrentUserContext { OrganizationId = Guid.NewGuid() },
            Configuration());
        var start = await service.StartAsync(CancellationToken.None);

        await service.CompleteAsync(start.CorrelationToken, "grant-token", CancellationToken.None);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CompleteAsync(start.CorrelationToken, "another-token", CancellationToken.None));

        Assert.Contains("expired or was already used", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, verifier.Calls);
    }

    [Fact]
    public async Task GetStatusAsync_ReturnsConnectedMetadata_WithoutSecrets()
    {
        var organizationId = Guid.NewGuid();
        var connectedAt = DateTimeOffset.UtcNow.AddDays(-2);
        var store = new FakeStore();
        store.Metadata[organizationId] = new EconomicConnectionMetadata(
            "123456",
            "Niels VVS",
            connectedAt,
            DateTimeOffset.UtcNow);
        var service = new EconomicConnectionService(
            store,
            new FakeVerifier(),
            new MutableCurrentUserContext { OrganizationId = organizationId },
            Configuration());

        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.True(status.Available);
        Assert.True(status.Connected);
        Assert.Equal("e-conomic", status.ProviderDisplayName);
        Assert.Equal("123456", status.AgreementNumber);
        Assert.Equal("Niels VVS", status.CompanyName);
        Assert.Equal(connectedAt, status.ConnectedAt);
    }

    private sealed class MutableCurrentUserContext : ICurrentUserContext
    {
        public Guid? UserId { get; init; } = Guid.NewGuid();
        public Guid? OrganizationId { get; set; }
        public string? Role { get; init; } = "Admin";
    }

    private sealed class FakeVerifier(EconomicAgreementIdentity? identity = null) : IEconomicConnectionVerifier
    {
        public int Calls { get; private set; }

        public Task<EconomicAgreementIdentity> VerifyGrantTokenAsync(string agreementGrantToken, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.False(string.IsNullOrWhiteSpace(agreementGrantToken));
            return Task.FromResult(identity ?? new EconomicAgreementIdentity("1", "Test Company"));
        }
    }

    private sealed class FakeStore : IEconomicConnectionStore
    {
        public Dictionary<string, (Guid OrganizationId, DateTimeOffset ExpiresAt)> Attempts { get; } = new(StringComparer.Ordinal);
        public Dictionary<Guid, (string Token, EconomicAgreementIdentity Identity)> Connections { get; } = new();
        public Dictionary<Guid, EconomicConnectionMetadata> Metadata { get; } = new();

        public Task<bool> HasConnectionAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(Connections.ContainsKey(organizationId) || Metadata.ContainsKey(organizationId));

        public Task<string?> GetAgreementGrantTokenAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(Connections.TryGetValue(organizationId, out var value) ? value.Token : null);

        public Task<EconomicConnectionMetadata?> GetConnectionMetadataAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(Metadata.TryGetValue(organizationId, out var value) ? value : null);

        public Task SaveConnectionAsync(Guid organizationId, string agreementGrantToken, EconomicAgreementIdentity identity, CancellationToken cancellationToken)
        {
            Connections[organizationId] = (agreementGrantToken, identity);
            var now = DateTimeOffset.UtcNow;
            Metadata[organizationId] = new EconomicConnectionMetadata(identity.AgreementNumber, identity.CompanyName, now, now);
            return Task.CompletedTask;
        }

        public Task DeleteConnectionAsync(Guid organizationId, CancellationToken cancellationToken)
        {
            Connections.Remove(organizationId);
            Metadata.Remove(organizationId);
            return Task.CompletedTask;
        }

        public Task SaveConnectAttemptAsync(Guid organizationId, string correlationHash, DateTimeOffset expiresAt, CancellationToken cancellationToken)
        {
            Attempts[correlationHash] = (organizationId, expiresAt);
            return Task.CompletedTask;
        }

        public Task<Guid?> ConsumeConnectAttemptAsync(string correlationHash, DateTimeOffset now, CancellationToken cancellationToken)
        {
            if (!Attempts.Remove(correlationHash, out var attempt) || attempt.ExpiresAt <= now)
                return Task.FromResult<Guid?>(null);
            return Task.FromResult<Guid?>(attempt.OrganizationId);
        }
    }
}
