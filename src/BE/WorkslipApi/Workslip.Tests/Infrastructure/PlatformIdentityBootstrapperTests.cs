using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Infrastructure;

public sealed class PlatformIdentityBootstrapperTests
{
    private const string ConfigKey = "WORKSLIP_SYNTHETIC_SUPERADMIN_EMAIL";
    private const string SyntheticEmail = "temporary-superadmin@example.test";
    private static readonly Guid RotatableId =
        new("F6F6F6F6-DA5B-4CC4-BBEB-07B40CAB806F");
    private static readonly Guid[] LegacyIds =
    [
        new("92779E5B-DA5B-4CC4-BBEB-07B40CAB806F"),
        new("D4D4D4D4-DA5B-4CC4-BBEB-07B40CAB806F"),
        new("E5E5E5E5-DA5B-4CC4-BBEB-07B40CAB806F")
    ];

    [Fact]
    public async Task BootstrapAsync_MissingConfiguredEmailFailsClosedBeforeGraphOrMutation()
    {
        await using var context = CreateContext();
        var entra = new FakeSuperadminEntraService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateSeeder(context, entra, configuredEmail: null).BootstrapAsync());

        Assert.Contains(ConfigKey, exception.Message, StringComparison.Ordinal);
        Assert.Empty(entra.EnsureCalls);
        Assert.Empty(await context.Organizations.ToListAsync());
        Assert.Empty(await context.Users.ToListAsync());
    }

    [Fact]
    public async Task BootstrapAsync_FreshDatabaseCreatesOneRotatableSuperadminAndIsIdempotent()
    {
        await using var context = CreateContext();
        var entra = new FakeSuperadminEntraService();
        var seeder = CreateSeeder(context, entra);

        await seeder.BootstrapAsync();
        await seeder.BootstrapAsync();

        var platform = Assert.Single(await context.Organizations.ToListAsync());
        Assert.Equal(PlatformOrganization.Id, platform.Id);
        var user = Assert.Single(await context.Users.ToListAsync());
        Assert.Equal(RotatableId, user.Id);
        Assert.Equal(PlatformOrganization.Id, user.OrganizationId);
        Assert.Equal(PlatformOrganization.Id, user.FilialId);
        Assert.Equal(SyntheticEmail, user.Email);
        Assert.Equal("Workslip Test Superadmin", user.DisplayName);
        Assert.Equal(Roles.Superadmin, user.Role);
        Assert.Equal(2, entra.EnsureCalls.Count);
        Assert.Empty(entra.RevokeCalls);
        Assert.Empty(entra.DeleteCalls);
        Assert.False(context.IsSeeding);
    }

    [Fact]
    public async Task BootstrapAsync_LegacyPlatformSuperadminsAreRemovedAndTheirEntraRolesRevoked()
    {
        await using var context = CreateContext();
        context.Organizations.Add(CreateOrganization(
            PlatformOrganization.Id,
            PlatformOrganization.Name,
            PlatformOrganization.Cvr));
        for (var index = 0; index < LegacyIds.Length; index++)
        {
            context.Users.Add(CreateUser(
                LegacyIds[index],
                PlatformOrganization.Id,
                $"legacy-{index}@example.test",
                $"legacy-entra-{index}"));
        }
        await context.SaveChangesAsync();
        var entra = new FakeSuperadminEntraService();

        await CreateSeeder(context, entra).BootstrapAsync();

        var user = Assert.Single(await context.Users.AsNoTracking().ToListAsync());
        Assert.Equal(RotatableId, user.Id);
        Assert.Equal(SyntheticEmail, user.Email);
        Assert.Equal(Roles.Superadmin, user.Role);
        Assert.Equal(
            ["legacy-entra-0", "legacy-entra-1", "legacy-entra-2"],
            entra.RevokeCalls.OrderBy(value => value).ToArray());
    }

    [Fact]
    public async Task BootstrapAsync_RotationReplacesPreviousSyntheticIdentityAndRevokesOldRole()
    {
        await using var context = CreateContext();
        var firstEntra = new FakeSuperadminEntraService();
        await CreateSeeder(context, firstEntra, "first-superadmin@example.test").BootstrapAsync();

        var secondEntra = new FakeSuperadminEntraService();
        await CreateSeeder(context, secondEntra, "second-superadmin@example.test").BootstrapAsync();

        var user = Assert.Single(await context.Users.AsNoTracking().ToListAsync());
        Assert.Equal(RotatableId, user.Id);
        Assert.Equal("second-superadmin@example.test", user.Email);
        Assert.Equal("entra-second-superadmin", user.EntraId);
        Assert.Equal(["entra-first-superadmin"], secondEntra.RevokeCalls);
    }

    [Fact]
    public async Task BootstrapAsync_ConfiguredEmailOwnedByOrdinaryUserRefusesPrivilegeEscalation()
    {
        await using var context = CreateContext();
        var customer = CreateOrganization(Guid.NewGuid(), "Customer", "12345678");
        context.Organizations.Add(customer);
        context.Users.Add(CreateUser(Guid.NewGuid(), customer.Id, SyntheticEmail, "ordinary-entra"));
        await context.SaveChangesAsync();
        var entra = new FakeSuperadminEntraService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateSeeder(context, entra).BootstrapAsync());

        Assert.Contains("non-bootstrap", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(entra.EnsureCalls);
        var ordinary = Assert.Single(await context.Users.AsNoTracking().ToListAsync());
        Assert.Equal(Roles.Admin, ordinary.Role);
        Assert.Equal(customer.Id, ordinary.OrganizationId);
    }

    [Fact]
    public async Task BootstrapAsync_LegacyUserWithTenantReferencesFailsBeforeGraph()
    {
        await using var context = CreateContext();
        var customer = CreateOrganization(Guid.NewGuid(), "Customer", "12345678");
        var legacy = CreateUser(LegacyIds[0], customer.Id, "legacy@example.test", "legacy-entra");
        context.Organizations.Add(customer);
        context.Users.Add(legacy);
        context.Worksheets.Add(new WorksheetRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = customer.Id,
            UserId = legacy.Id,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        var entra = new FakeSuperadminEntraService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateSeeder(context, entra).BootstrapAsync());

        Assert.Contains("tenant-bound", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(entra.EnsureCalls);
        Assert.Equal(customer.Id, (await context.Users.SingleAsync()).OrganizationId);
    }

    private static PlatformIdentityBootstrapper CreateSeeder(
        SqlDbContext context,
        ISuperadminEntraService entra,
        string? configuredEmail = SyntheticEmail)
    {
        var values = new Dictionary<string, string?>();
        if (configuredEmail is not null)
            values[ConfigKey] = configuredEmail;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new PlatformIdentityBootstrapper(
            context,
            entra,
            configuration,
            NullLogger<PlatformIdentityBootstrapper>.Instance);
    }

    private static SqlDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SqlDbContext(options);
    }

    private static OrganizationRow CreateOrganization(Guid id, string name, string cvr) => new()
    {
        Id = id,
        Name = name,
        Cvr = cvr,
        CreatedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
    };

    private static UserDataRow CreateUser(
        Guid id,
        Guid organizationId,
        string email,
        string entraId) => new()
    {
        Id = id,
        OrganizationId = organizationId,
        FilialId = organizationId,
        Email = email,
        DisplayName = $"Original {id:N}",
        Phone = string.Empty,
        EntraId = entraId,
        EntraEmail = $"{entraId}#EXT#@tenant.onmicrosoft.com",
        Role = Roles.Admin,
        CreatedAt = DateTimeOffset.Parse("2025-01-02T00:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2025-01-02T00:00:00Z")
    };

    private sealed class FakeSuperadminEntraService : ISuperadminEntraService
    {
        public List<string> EnsureCalls { get; } = [];
        public List<string> RevokeCalls { get; } = [];
        public List<string> DeleteCalls { get; } = [];

        public Task<CreateEntraUserResult> EnsureSuperadminAsync(
            string email,
            string displayName,
            CancellationToken cancellationToken)
        {
            EnsureCalls.Add(email);
            var localPart = email.Split('@')[0];
            return Task.FromResult(new CreateEntraUserResult(
                $"entra-{localPart}",
                $"{localPart}#EXT#@tenant.onmicrosoft.com",
                displayName,
                Created: false));
        }

        public Task RevokeSuperadminAsync(string entraUserId, CancellationToken cancellationToken)
        {
            RevokeCalls.Add(entraUserId);
            return Task.CompletedTask;
        }

        public Task DeleteUserAsync(string entraUserId, CancellationToken ct)
        {
            DeleteCalls.Add(entraUserId);
            return Task.CompletedTask;
        }

        public Task<CreateEntraUserResult> CreateUserAsync(
            string email,
            string displayName,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<CreateEntraUserResult> EnsureInvitedUserAsync(
            string email,
            CancellationToken ct) => throw new NotSupportedException();

        public Task<CreateEntraUserResult> InviteAdminAsync(
            string email,
            string displayName,
            CancellationToken ct) => throw new NotSupportedException();
    }
}
