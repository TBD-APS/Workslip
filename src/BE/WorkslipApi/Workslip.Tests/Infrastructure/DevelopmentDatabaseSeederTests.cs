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
    public async Task SeedAsync_ReconcilesOrganizationBoundSuperadminsInDatabaseAndEntra()
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

        Assert.Equal(organization.Id, rasmus.OrganizationId);
        Assert.Equal("rasmusvm6@hotmail.com", rasmus.Email);
        Assert.Equal("Rasmus Bak Jakobsen", rasmus.DisplayName);
        Assert.Equal("28929173", rasmus.Phone);
        Assert.Equal(Roles.Superadmin, rasmus.Role);
        Assert.Equal("entra-rasmus-id", rasmus.EntraId);
        Assert.Equal("rasmusvm6_hotmail.com#EXT#@tenant.onmicrosoft.com", rasmus.EntraEmail);

        Assert.Equal(organization.Id, mahad.OrganizationId);
        Assert.Equal("mahad8@outlook.dk", mahad.Email);
        Assert.Equal("Mahad", mahad.DisplayName);
        Assert.Equal(Roles.Superadmin, mahad.Role);
        Assert.Equal("entra-mahad-id", mahad.EntraId);
        Assert.Equal("mahad8_outlook.dk#EXT#@tenant.onmicrosoft.com", mahad.EntraEmail);

        Assert.Equal(4, entraService.EnsureSuperadminCalls);
        Assert.Equal(0, entraService.DeleteCalls);
        Assert.Equal(5, await context.Users.CountAsync());
        Assert.False(context.IsSeeding);
    }

    [Fact]
    public async Task SeedAsync_WhenCanonicalRasmusEmailAlreadyBelongsToAnotherId_FailsBeforeGraphCall()
    {
        await using var context = CreateContext();
        var organization = CreateOrganization(Guid.NewGuid());
        var timestamp = DateTimeOffset.Parse("2025-01-02T00:00:00Z");
        context.Organizations.Add(organization);
        context.Users.Add(new UserDataRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            DisplayName = "Conflicting canonical email",
            Email = "rasmusvm6@hotmail.com",
            Phone = "22222222",
            Role = Roles.Admin,
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

        Assert.Contains("was not created", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, entraService.EnsureSuperadminCalls);
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
        public int EnsureSuperadminCalls { get; private set; }
        public int DeleteCalls { get; private set; }

        public Task<CreateEntraUserResult> EnsureSuperadminAsync(
            string email,
            string displayName,
            CancellationToken cancellationToken)
        {
            EnsureSuperadminCalls++;

            return email switch
            {
                "rasmusvm6@hotmail.com" => Task.FromResult(new CreateEntraUserResult(
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
}
