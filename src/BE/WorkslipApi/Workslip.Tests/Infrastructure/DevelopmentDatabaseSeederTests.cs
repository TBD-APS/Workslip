using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Infrastructure;

public sealed class DevelopmentDatabaseSeederTests
{
    private static readonly Guid CanonicalRasmusId =
        new("92779E5B-DA5B-4CC4-BBEB-07B40CAB806F");

    private static readonly Guid CanonicalMahadId =
        new("D4D4D4D4-DA5B-4CC4-BBEB-07B40CAB806F");

    [Fact]
    public async Task SeedAsync_FreshCustomerDatabaseCreatesStablePlatformMembership()
    {
        await using var context = CreateContext();
        var customer = CreateOrganization(Guid.NewGuid(), "Customer", "12345678");
        context.Organizations.Add(customer);
        await context.SaveChangesAsync();
        var entra = new FakeSuperadminEntraService();
        var seeder = CreateSeeder(context, entra);

        await seeder.SeedAsync();
        var firstSnapshot = await SnapshotAsync(context);
        await seeder.SeedAsync();
        var secondSnapshot = await SnapshotAsync(context);

        Assert.Equal(firstSnapshot, secondSnapshot);
        Assert.Equal(2, await context.Organizations.CountAsync());
        Assert.Equal(5, await context.Users.CountAsync());
        var platform = await context.Organizations.SingleAsync(
            organization => organization.Id == PlatformOrganization.Id);
        Assert.Equal(PlatformOrganization.Name, platform.Name);
        Assert.Equal(PlatformOrganization.Cvr, platform.Cvr);
        var superadmins = await context.Users
            .Where(user => user.Id == CanonicalRasmusId || user.Id == CanonicalMahadId)
            .ToListAsync();
        Assert.All(superadmins, user => Assert.Equal(PlatformOrganization.Id, user.OrganizationId));
        Assert.All(superadmins, user => Assert.Equal(Roles.Superadmin, user.Role));
        var ordinaryUsers = await context.Users
            .Where(user => user.Id != CanonicalRasmusId && user.Id != CanonicalMahadId)
            .ToListAsync();
        Assert.Equal(3, ordinaryUsers.Count);
        Assert.All(ordinaryUsers, user => Assert.Equal(customer.Id, user.OrganizationId));
        Assert.Equal(4, entra.EnsureCalls.Count);
        Assert.Empty(entra.DeleteCalls);
        Assert.False(context.IsSeeding);
    }

    [Fact]
    public async Task SeedAsync_ExistingPlatformRowsRepairsAllCanonicalFieldsAndPreservesCustomerUser()
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
        context.Organizations.AddRange(customer, platform);
        context.Users.AddRange(unrelated, rasmus, mahad);
        await context.SaveChangesAsync();

        await CreateSeeder(context, new FakeSuperadminEntraService()).SeedAsync();

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
        var storedUnrelated = await context.Users.AsNoTracking().SingleAsync(user => user.Id == unrelated.Id);
        Assert.Equal(customer.Id, storedUnrelated.OrganizationId);
        Assert.Equal(unrelated.DisplayName, storedUnrelated.DisplayName);
        Assert.Equal(unrelated.Role, storedUnrelated.Role);
    }

    [Theory]
    [InlineData(" RASMUSVM6@HOTMAIL.COM ")]
    [InlineData(" MAHAD8@OUTLOOK.DK ")]
    public async Task SeedAsync_NormalizedCanonicalEmailOwnedByAnotherIdFailsBeforeMutationOrGraph(
        string conflictingEmail)
    {
        await using var context = CreateContext();
        var customer = CreateOrganization(Guid.NewGuid(), "Customer", "12345678");
        var conflict = CreateUser(Guid.NewGuid(), customer.Id, conflictingEmail);
        context.Organizations.Add(customer);
        context.Users.Add(conflict);
        await context.SaveChangesAsync();
        var entra = new FakeSuperadminEntraService();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSeeder(context, entra).SeedAsync());

        Assert.Contains("conflict", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(entra.EnsureCalls);
        Assert.Single(await context.Organizations.ToListAsync());
        Assert.Single(await context.Users.ToListAsync());
        Assert.False(await context.Organizations.AnyAsync(
            organization => organization.Id == PlatformOrganization.Id));
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
            () => CreateSeeder(context, entra).SeedAsync());

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
            () => CreateSeeder(context, entra).SeedAsync());

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
            () => CreateSeeder(context, entra).SeedAsync());

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
            () => CreateSeeder(context, entra).SeedAsync());

        Assert.Contains(PlatformOrganization.Cvr, exception.Message, StringComparison.Ordinal);
        Assert.Empty(entra.EnsureCalls);
        Assert.Single(await context.Organizations.ToListAsync());
        Assert.Empty(await context.Users.ToListAsync());
    }

    private static DevelopmentDatabaseSeeder CreateSeeder(
        SqlDbContext context,
        ISuperadminEntraService entra) =>
        new(
            context,
            new InstallationBaselineProvisioner(context),
            entra,
            NullLogger<DevelopmentDatabaseSeeder>.Instance);

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
            var localPart = email.StartsWith("rasmus", StringComparison.Ordinal)
                ? "rasmus"
                : "mahad";
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
