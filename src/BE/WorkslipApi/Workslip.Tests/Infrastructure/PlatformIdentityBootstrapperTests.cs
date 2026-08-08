using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Infrastructure;

public sealed class PlatformIdentityBootstrapperTests
{
    private static readonly Guid CanonicalRasmusId =
        new("92779E5B-DA5B-4CC4-BBEB-07B40CAB806F");

    private static readonly Guid CanonicalMahadId =
        new("D4D4D4D4-DA5B-4CC4-BBEB-07B40CAB806F");

    private static readonly Guid CanonicalMathiasCreateId =
        new("E5E5E5E5-DA5B-4CC4-BBEB-07B40CAB806F");

    [Fact]
    public async Task BootstrapAsync_FreshCustomerDatabaseCreatesExactlyThreeStablePlatformSuperadmins()
    {
        await using var context = CreateContext();
        var customer = CreateOrganization(Guid.NewGuid(), "Customer", "12345678");
        context.Organizations.Add(customer);
        await context.SaveChangesAsync();
        var entra = new FakeSuperadminEntraService();
        var seeder = CreateSeeder(context, entra);

        await seeder.BootstrapAsync();
        var firstSnapshot = await SnapshotAsync(context);
        await seeder.BootstrapAsync();
        var secondSnapshot = await SnapshotAsync(context);

        Assert.Equal(firstSnapshot, secondSnapshot);
        Assert.Equal(2, await context.Organizations.CountAsync());
        Assert.Equal(3, await context.Users.CountAsync());
        var platform = await context.Organizations.SingleAsync(
            organization => organization.Id == PlatformOrganization.Id);
        Assert.Equal(PlatformOrganization.Name, platform.Name);
        Assert.Equal(PlatformOrganization.Cvr, platform.Cvr);
        var superadmins = await context.Users
            .Where(user => user.Id == CanonicalRasmusId ||
                           user.Id == CanonicalMahadId ||
                           user.Id == CanonicalMathiasCreateId)
            .ToListAsync();
        Assert.Equal(3, superadmins.Count);
        Assert.All(superadmins, user => Assert.Equal(PlatformOrganization.Id, user.OrganizationId));
        Assert.All(superadmins, user => Assert.Equal(Roles.Superadmin, user.Role));
        Assert.Equal(6, entra.EnsureCalls.Count);
        Assert.Equal(2, entra.EnsureCalls.Count(email => email == "mathiaslt1@hotmail.dk"));
        Assert.Empty(entra.DeleteCalls);
        Assert.False(context.IsSeeding);
    }

    [Fact]
    public async Task DevelopmentSeeder_ComposesPlatformBootstrapWithSeparateDemoSeed()
    {
        await using var context = CreateContext();
        var entra = new FakeSuperadminEntraService();
        var developmentSeeder = new DevelopmentDatabaseSeeder(
            context,
            new InstallationBaselineProvisioner(context),
            entra,
            NullLogger<DevelopmentDatabaseSeeder>.Instance);

        await developmentSeeder.SeedAsync();

        Assert.Equal(2, await context.Organizations.CountAsync());
        Assert.Equal(6, await context.Users.CountAsync());
        Assert.Equal(3, await context.Users.CountAsync(user => user.Role == Roles.Superadmin));
        Assert.Equal(3, await context.Users.CountAsync(user => user.Role != Roles.Superadmin));
    }

    [Fact]
    public async Task BootstrapAsync_ExistingRowsPreserveStableIdsAndValidEntraBindingsWhileReconcilingFields()
    {
        await using var context = CreateContext();
        var customer = CreateOrganization(Guid.NewGuid(), "Customer", "12345678");
        var platform = CreateOrganization(
            PlatformOrganization.Id,
            PlatformOrganization.Name,
            PlatformOrganization.Cvr);
        var unrelated = CreateUser(Guid.NewGuid(), customer.Id, "unrelated@example.test");
        var rasmus = CreateUser(CanonicalRasmusId, platform.Id, "rasmusvm6@hotmail.com");
        var mahad = CreateUser(CanonicalMahadId, platform.Id, "mahad8@outlook.dk");
        var existingMathiasId = Guid.NewGuid();
        var mathias = CreateUser(existingMathiasId, platform.Id, "mathiaslt1@hotmail.dk");
        SetValidEntraBinding(rasmus);
        SetValidEntraBinding(mahad);
        SetValidEntraBinding(mathias);
        context.Organizations.AddRange(customer, platform);
        context.Users.AddRange(unrelated, rasmus, mahad, mathias);
        await context.SaveChangesAsync();

        await CreateSeeder(context, new FakeSuperadminEntraService()).BootstrapAsync();

        var storedRasmus = await context.Users.AsNoTracking().SingleAsync(user => user.Id == CanonicalRasmusId);
        Assert.Equal(PlatformOrganization.Id, storedRasmus.OrganizationId);
        Assert.Equal("Rasmus Bak Jakobsen", storedRasmus.DisplayName);
        Assert.Equal("28929173", storedRasmus.Phone);
        Assert.Equal("rasmusvm6@hotmail.com", storedRasmus.Email);
        Assert.Equal(Roles.Superadmin, storedRasmus.Role);
        Assert.Equal("entra-rasmus", storedRasmus.EntraId);
        Assert.Equal("rasmus#EXT#@tenant.onmicrosoft.com", storedRasmus.EntraEmail);
        var storedMahad = await context.Users.AsNoTracking().SingleAsync(user => user.Id == CanonicalMahadId);
        Assert.Equal(PlatformOrganization.Id, storedMahad.OrganizationId);
        Assert.Equal("Mahad", storedMahad.DisplayName);
        Assert.Equal(string.Empty, storedMahad.Phone);
        Assert.Equal("mahad8@outlook.dk", storedMahad.Email);
        Assert.Equal(Roles.Superadmin, storedMahad.Role);
        Assert.Equal("entra-mahad", storedMahad.EntraId);
        Assert.Equal("mahad#EXT#@tenant.onmicrosoft.com", storedMahad.EntraEmail);
        var storedMathias = await context.Users.AsNoTracking().SingleAsync(user => user.Id == existingMathiasId);
        Assert.Equal(existingMathiasId, storedMathias.Id);
        Assert.Equal(PlatformOrganization.Id, storedMathias.OrganizationId);
        Assert.Equal("Mathias Lambæk", storedMathias.DisplayName);
        Assert.Equal("mathiaslt1@hotmail.dk", storedMathias.Email);
        Assert.Equal(Roles.Superadmin, storedMathias.Role);
        Assert.Equal("entra-mathias", storedMathias.EntraId);
        Assert.Equal("mathias#EXT#@tenant.onmicrosoft.com", storedMathias.EntraEmail);
        Assert.False(await context.Users.AnyAsync(user => user.Id == CanonicalMathiasCreateId));
        var storedUnrelated = await context.Users.AsNoTracking().SingleAsync(user => user.Id == unrelated.Id);
        Assert.Equal(customer.Id, storedUnrelated.OrganizationId);
        Assert.Equal(unrelated.DisplayName, storedUnrelated.DisplayName);
        Assert.Equal(unrelated.Role, storedUnrelated.Role);
    }

    [Theory]
    [InlineData(" RASMUSVM6@HOTMAIL.COM ")]
    [InlineData(" MAHAD8@OUTLOOK.DK ")]
    [InlineData(" MATHIASLT1@HOTMAIL.DK ")]
    public async Task BootstrapAsync_NormalizedEmailOnExistingRowPreservesItsStableWorkslipId(
        string canonicalEmail)
    {
        await using var context = CreateContext();
        var platform = CreateOrganization(
            PlatformOrganization.Id,
            PlatformOrganization.Name,
            PlatformOrganization.Cvr);
        var existing = CreateUser(Guid.NewGuid(), platform.Id, canonicalEmail);
        SetValidEntraBinding(existing);
        context.Organizations.Add(platform);
        context.Users.Add(existing);
        await context.SaveChangesAsync();
        var entra = new FakeSuperadminEntraService();

        await CreateSeeder(context, entra).BootstrapAsync();

        var stored = await context.Users.AsNoTracking().SingleAsync(user => user.Id == existing.Id);
        Assert.Equal(existing.Id, stored.Id);
        Assert.Equal(PlatformOrganization.Id, stored.OrganizationId);
        Assert.Equal(Roles.Superadmin, stored.Role);
        Assert.Equal(3, entra.EnsureCalls.Count);
    }

    [Fact]
    public async Task BootstrapAsync_ExistingEntraObjectIdDiffersFromGraph_PreservesBindingAndRollsBack()
    {
        await using var context = CreateContext();
        var platform = CreateOrganization(
            PlatformOrganization.Id,
            PlatformOrganization.Name,
            PlatformOrganization.Cvr);
        var mathias = CreateUser(CanonicalMathiasCreateId, platform.Id, "mathiaslt1@hotmail.dk");
        mathias.EntraId = "existing-valid-entra-object-id";
        context.Organizations.Add(platform);
        context.Users.Add(mathias);
        await context.SaveChangesAsync();
        var original = await SnapshotAsync(context);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateSeeder(context, new FakeSuperadminEntraService()).BootstrapAsync());

        Assert.Contains("existing binding was preserved", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, await SnapshotAsync(context));
    }

    [Fact]
    public async Task SeedAsync_CanonicalUserWithTenantReferenceFailsBeforeMutationOrGraph()
    {
        await using var context = CreateContext();
        var customer = CreateOrganization(Guid.NewGuid(), "Customer", "12345678");
        var rasmus = CreateUser(CanonicalRasmusId, customer.Id, "rasmusvm6@hotmail.com");
        context.Organizations.Add(customer);
        context.Users.Add(rasmus);
        context.JobAssignments.Add(new JobAssignmentRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = customer.Id,
            ReportId = Guid.NewGuid(),
            UserId = rasmus.Id,
            AssignedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        var entra = new FakeSuperadminEntraService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSeeder(context, entra).BootstrapAsync());

        Assert.Contains("tenant", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(entra.EnsureCalls);
        Assert.Equal(customer.Id, (await context.Users.SingleAsync()).OrganizationId);
        Assert.False(await context.Organizations.AnyAsync(
            organization => organization.Id == PlatformOrganization.Id));
    }

    [Fact]
    public async Task SeedAsync_ExactReservedOrganizationWithNonCanonicalUserFailsWithoutRepair()
    {
        await using var context = CreateContext();
        var platform = CreateOrganization(
            PlatformOrganization.Id,
            "Customer using reserved identity",
            PlatformOrganization.Cvr);
        context.Organizations.Add(platform);
        context.Users.Add(CreateUser(Guid.NewGuid(), platform.Id, "customer@example.test"));
        await context.SaveChangesAsync();
        var entra = new FakeSuperadminEntraService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSeeder(context, entra).BootstrapAsync());

        Assert.Contains("reserved", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(entra.EnsureCalls);
        Assert.Equal(
            "Customer using reserved identity",
            (await context.Organizations.AsNoTracking().SingleAsync()).Name);
        Assert.Single(await context.Users.ToListAsync());
    }

    [Fact]
    public async Task SeedAsync_ExactReservedOrganizationWithOperationalDataFailsWithoutRepair()
    {
        await using var context = CreateContext();
        var platform = CreateOrganization(
            PlatformOrganization.Id,
            "Customer using reserved identity",
            PlatformOrganization.Cvr);
        context.Organizations.Add(platform);
        context.Customers.Add(new CustomerRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = platform.Id,
            Name = "Operational customer",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        var entra = new FakeSuperadminEntraService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSeeder(context, entra).BootstrapAsync());

        Assert.Contains("reserved", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(entra.EnsureCalls);
        Assert.Equal(
            "Customer using reserved identity",
            (await context.Organizations.AsNoTracking().SingleAsync()).Name);
        Assert.Single(await context.Customers.ToListAsync());
    }

    [Fact]
    public async Task SeedAsync_ReservedCvrOnDifferentIdFailsBeforeMutationOrGraph()
    {
        await using var context = CreateContext();
        var customer = CreateOrganization(Guid.NewGuid(), "Customer", PlatformOrganization.Cvr);
        context.Organizations.Add(customer);
        await context.SaveChangesAsync();
        var entra = new FakeSuperadminEntraService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSeeder(context, entra).BootstrapAsync());

        Assert.Contains(PlatformOrganization.Cvr, exception.Message, StringComparison.Ordinal);
        Assert.Empty(entra.EnsureCalls);
        Assert.Single(await context.Organizations.ToListAsync());
        Assert.Empty(await context.Users.ToListAsync());
    }

    private static PlatformIdentityBootstrapper CreateSeeder(
        SqlDbContext context,
        ISuperadminEntraService entra) =>
        new(context, entra, NullLogger<PlatformIdentityBootstrapper>.Instance);

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

    private static UserDataRow CreateUser(Guid id, Guid organizationId, string email) => new()
    {
        Id = id,
        OrganizationId = organizationId,
        Email = email,
        DisplayName = $"Original {id:N}",
        Phone = "old-phone",
        EntraId = "old-entra",
        EntraEmail = "old-entra@example.test",
        Role = Roles.Admin,
        CreatedAt = DateTimeOffset.Parse("2025-01-02T00:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2025-01-02T00:00:00Z")
    };

    private static void SetValidEntraBinding(UserDataRow user)
    {
        var localPart = ResolveLocalPart(user.Email);
        user.EntraId = $"entra-{localPart}";
        user.EntraEmail = $"{localPart}#EXT#@tenant.onmicrosoft.com";
    }

    private static string ResolveLocalPart(string email)
    {
        var normalizedEmail = email.Trim();
        if (normalizedEmail.StartsWith("rasmus", StringComparison.OrdinalIgnoreCase))
            return "rasmus";
        if (normalizedEmail.StartsWith("mahad", StringComparison.OrdinalIgnoreCase))
            return "mahad";
        if (normalizedEmail.StartsWith("mathias", StringComparison.OrdinalIgnoreCase))
            return "mathias";

        throw new InvalidOperationException($"Unexpected canonical email '{email}'.");
    }

    private static async Task<UserSnapshot[]> SnapshotAsync(SqlDbContext context) =>
        await context.Users
            .AsNoTracking()
            .OrderBy(user => user.Id)
            .Select(user => new UserSnapshot(
                user.Id,
                user.OrganizationId,
                user.Email,
                user.DisplayName,
                user.Phone,
                user.EntraId,
                user.EntraEmail,
                user.Role,
                user.CreatedAt,
                user.UpdatedAt))
            .ToArrayAsync();

    private sealed class FakeSuperadminEntraService : ISuperadminEntraService
    {
        public List<string> EnsureCalls { get; } = [];
        public List<string> DeleteCalls { get; } = [];

        public Task<CreateEntraUserResult> EnsureSuperadminAsync(
            string email,
            string displayName,
            CancellationToken cancellationToken)
        {
            EnsureCalls.Add(email);
            var localPart = ResolveLocalPart(email);
            return Task.FromResult(new CreateEntraUserResult(
                $"entra-{localPart}",
                $"{localPart}#EXT#@tenant.onmicrosoft.com",
                displayName,
                Created: false));
        }

        public Task DeleteUserAsync(string entraUserId, CancellationToken ct)
        {
            DeleteCalls.Add(entraUserId);
            return Task.CompletedTask;
        }

        public Task<CreateEntraUserResult> CreateUserAsync(
            string email,
            string displayName,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<CreateEntraUserResult> EnsureInvitedUserAsync(
            string email,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<CreateEntraUserResult> InviteAdminAsync(
            string email,
            string displayName,
            CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed record UserSnapshot(
        Guid Id,
        Guid OrganizationId,
        string Email,
        string DisplayName,
        string Phone,
        string EntraId,
        string EntraEmail,
        string Role,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
