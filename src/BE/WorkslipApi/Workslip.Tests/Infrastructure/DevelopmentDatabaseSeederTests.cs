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
    private static readonly Guid CanonicalRasmusSuperadminId =
        new("92779E5B-DA5B-4CC4-BBEB-07B40CAB806F");

    private static readonly Guid CanonicalMahadSuperadminId =
        new("D4D4D4D4-DA5B-4CC4-BBEB-07B40CAB806F");

    [Fact]
<<<<<<< HEAD
    public async Task SeedAsync_ReconcilesBothCanonicalSuperadminsInDatabaseAndEntra()
=======
    public async Task SeedAsync_ReconcilesOrganizationBoundSuperadminsInDatabaseAndEntra()
>>>>>>> 15649a93cfa5e16a9a7d40cb7a5e2865484d9a06
    {
        await using var context = CreateContext();
        var organization = CreateOrganization(Guid.NewGuid());
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        var entraService = new FakeSuperadminEntraService();
        var seeder = new DevelopmentDatabaseSeeder(
            context,
            entraService,
            NullLogger<DevelopmentDatabaseSeeder>.Instance);

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        var rasmus = await context.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == CanonicalRasmusSuperadminId);
        var mahad = await context.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == CanonicalMahadSuperadminId);

<<<<<<< HEAD
        Assert.Null(rasmus.OrganizationId);
=======
        Assert.Equal(organization.Id, rasmus.OrganizationId);
>>>>>>> 15649a93cfa5e16a9a7d40cb7a5e2865484d9a06
        Assert.Equal("rasmusvm6@hotmail.com", rasmus.Email);
        Assert.Equal("Rasmus Bak Jakobsen", rasmus.DisplayName);
        Assert.Equal("28929173", rasmus.Phone);
        Assert.Equal(Roles.Superadmin, rasmus.Role);
<<<<<<< HEAD
        Assert.Equal("entra-rasmus", rasmus.EntraId);
        Assert.Equal("rasmusvm6_hotmail.com#EXT#@tenant.onmicrosoft.com", rasmus.EntraEmail);

        Assert.Null(mahad.OrganizationId);
        Assert.Equal("mahad8@outlook.dk", mahad.Email);
        Assert.Equal("Mahad", mahad.DisplayName);
        Assert.Equal(string.Empty, mahad.Phone);
        Assert.Equal(Roles.Superadmin, mahad.Role);
        Assert.Equal("entra-mahad", mahad.EntraId);
        Assert.Equal("mahad8_outlook.dk#EXT#@tenant.onmicrosoft.com", mahad.EntraEmail);

        Assert.Equal(4, entraService.EnsureCalls.Count);
        Assert.Equal(2, entraService.EnsureCalls.Count(call => call.Email == "rasmusvm6@hotmail.com"));
        Assert.Equal(2, entraService.EnsureCalls.Count(call => call.Email == "mahad8@outlook.dk"));
=======
        Assert.Equal("entra-rasmus-id", rasmus.EntraId);
        Assert.Equal("rasmusvm6_hotmail.com#EXT#@tenant.onmicrosoft.com", rasmus.EntraEmail);

        Assert.Equal(organization.Id, mahad.OrganizationId);
        Assert.Equal("mahad8@outlook.dk", mahad.Email);
        Assert.Equal("Mahad", mahad.DisplayName);
        Assert.Equal(Roles.Superadmin, mahad.Role);
        Assert.Equal("entra-mahad-id", mahad.EntraId);
        Assert.Equal("mahad8_outlook.dk#EXT#@tenant.onmicrosoft.com", mahad.EntraEmail);

        Assert.Equal(4, entraService.EnsureSuperadminCalls);
>>>>>>> 15649a93cfa5e16a9a7d40cb7a5e2865484d9a06
        Assert.Equal(0, entraService.DeleteCalls);
        Assert.Equal(5, await context.Users.CountAsync());
        Assert.False(context.IsSeeding);
    }

    [Fact]
<<<<<<< HEAD
    public async Task SeedAsync_WhenMahadEmailBelongsToAnotherId_FailsBeforeGraphCall()
=======
    public async Task SeedAsync_WhenCanonicalRasmusEmailAlreadyBelongsToAnotherId_FailsBeforeGraphCall()
>>>>>>> 15649a93cfa5e16a9a7d40cb7a5e2865484d9a06
    {
        await using var context = CreateContext();
        var organization = CreateOrganization(Guid.NewGuid());
        var timestamp = DateTimeOffset.Parse("2025-01-02T00:00:00Z");
        context.Organizations.Add(organization);
        context.Users.Add(new UserDataRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            DisplayName = "Conflicting Mahad identity",
            Email = "mahad8@outlook.dk",
            Phone = string.Empty,
            Role = Roles.User,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        });
        await context.SaveChangesAsync();

        var entraService = new FakeSuperadminEntraService();
        var seeder = new DevelopmentDatabaseSeeder(
            context,
            entraService,
            NullLogger<DevelopmentDatabaseSeeder>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync());

        Assert.Contains("identity conflict", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(entraService.EnsureCalls);
        Assert.False(context.IsSeeding);
    }

    [Fact]
    public async Task SeedAsync_WhenCanonicalMahadEmailBelongsToAnotherId_FailsBeforeGraphCall()
    {
        await using var context = CreateContext();
        var organization = CreateOrganization(Guid.NewGuid());
        var timestamp = DateTimeOffset.Parse("2025-01-02T00:00:00Z");
        context.Organizations.Add(organization);
        context.Users.Add(new UserDataRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            DisplayName = "Conflicting Mahad identity",
            Email = "mahad8@outlook.dk",
            Phone = string.Empty,
            Role = Roles.User,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        });
        await context.SaveChangesAsync();

        var entraService = new FakeSuperadminEntraService();
        var seeder = new DevelopmentDatabaseSeeder(
            context,
            entraService,
            NullLogger<DevelopmentDatabaseSeeder>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SeedAsync());

        Assert.Contains("identity conflict", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, entraService.EnsureSuperadminCalls);
        Assert.False(context.IsSeeding);
    }

    private static SqlDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new SqlDbContext(options);
    }

    private static OrganizationRow CreateOrganization(Guid id) => new()
    {
        Id = id,
        Name = "Development organization",
        Cvr = "12345678",
        CreatedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z"),
        UpdatedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
    };

    private sealed class FakeSuperadminEntraService : ISuperadminEntraService
    {
<<<<<<< HEAD
        public List<EnsureCall> EnsureCalls { get; } = [];
=======
        public int EnsureSuperadminCalls { get; private set; }
>>>>>>> 15649a93cfa5e16a9a7d40cb7a5e2865484d9a06
        public int DeleteCalls { get; private set; }

        public Task<CreateEntraUserResult> EnsureSuperadminAsync(
            string email,
            string displayName,
            CancellationToken cancellationToken)
        {
<<<<<<< HEAD
            EnsureCalls.Add(new EnsureCall(email, displayName));
=======
            EnsureSuperadminCalls++;
>>>>>>> 15649a93cfa5e16a9a7d40cb7a5e2865484d9a06

            return email switch
            {
                "rasmusvm6@hotmail.com" => Task.FromResult(new CreateEntraUserResult(
<<<<<<< HEAD
                    "entra-rasmus",
                    "rasmusvm6_hotmail.com#EXT#@tenant.onmicrosoft.com",
                    "Rasmus Bak Jakobsen",
                    Created: false)),
                "mahad8@outlook.dk" => Task.FromResult(new CreateEntraUserResult(
                    "entra-mahad",
                    "mahad8_outlook.dk#EXT#@tenant.onmicrosoft.com",
                    "Mahad",
                    Created: false)),
                _ => throw new InvalidOperationException($"Unexpected Superadmin seed email '{email}'.")
=======
                    "entra-rasmus-id",
                    "rasmusvm6_hotmail.com#EXT#@tenant.onmicrosoft.com",
                    displayName,
                    Created: false)),
                "mahad8@outlook.dk" => Task.FromResult(new CreateEntraUserResult(
                    "entra-mahad-id",
                    "mahad8_outlook.dk#EXT#@tenant.onmicrosoft.com",
                    displayName,
                    Created: false)),
                _ => throw new InvalidOperationException($"Unexpected Superadmin email '{email}'.")
>>>>>>> 15649a93cfa5e16a9a7d40cb7a5e2865484d9a06
            };
        }

        public Task<CreateEntraUserResult> CreateUserAsync(
            string email,
            string displayName,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<CreateEntraUserResult> EnsureInvitedUserAsync(string email, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<CreateEntraUserResult> InviteAdminAsync(
            string email,
            string displayName,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public Task DeleteUserAsync(string entraUserId, CancellationToken ct)
        {
            DeleteCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed record EnsureCall(string Email, string DisplayName);
}
