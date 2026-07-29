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
    private static readonly Guid CanonicalSuperadminId =
        new("92779E5B-DA5B-4CC4-BBEB-07B40CAB806F");

    [Fact]
    public async Task SeedAsync_ReconcilesCanonicalSuperadminInDatabaseAndEntra()
    {
        await using var context = CreateContext();
        var organization = CreateOrganization(Guid.NewGuid());
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        var entraService = new FakeSuperadminEntraService
        {
            Result = new CreateEntraUserResult(
                "entra-superadmin-id",
                "rasmusvm6_hotmail.com#EXT#@tenant.onmicrosoft.com",
                "Rasmus Bak Jakobsen",
                Created: false)
        };
        var seeder = new DevelopmentDatabaseSeeder(
            context,
            entraService,
            NullLogger<DevelopmentDatabaseSeeder>.Instance);

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        var user = await context.Users
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Email == "rasmusvm6@hotmail.com");

        Assert.Equal(CanonicalSuperadminId, user.Id);
        Assert.Equal(organization.Id, user.OrganizationId);
        Assert.Equal("Rasmus Bak Jakobsen", user.DisplayName);
        Assert.Equal("28929173", user.Phone);
        Assert.Equal(Roles.Superadmin, user.Role);
        Assert.Equal("entra-superadmin-id", user.EntraId);
        Assert.Equal("rasmusvm6_hotmail.com#EXT#@tenant.onmicrosoft.com", user.EntraEmail);
        Assert.Equal(2, entraService.EnsureSuperadminCalls);
        Assert.Equal(0, entraService.DeleteCalls);
        Assert.Equal(4, await context.Users.CountAsync());
        Assert.False(context.IsSeeding);
    }

    [Fact]
    public async Task SeedAsync_WhenCanonicalIdAndEmailBelongToDifferentUsers_FailsBeforeGraphCall()
    {
        await using var context = CreateContext();
        var organization = CreateOrganization(Guid.NewGuid());
        var timestamp = DateTimeOffset.Parse("2025-01-02T00:00:00Z");
        context.Organizations.Add(organization);
        context.Users.AddRange(
            new UserDataRow
            {
                Id = CanonicalSuperadminId,
                OrganizationId = organization.Id,
                DisplayName = "Renamed canonical ID",
                Email = "renamed@example.test",
                Phone = "11111111",
                Role = Roles.User,
                CreatedAt = timestamp,
                UpdatedAt = timestamp
            },
            new UserDataRow
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
        public CreateEntraUserResult Result { get; init; } =
            new("entra-superadmin-id", "rasmusvm6@hotmail.com", "Rasmus Bak Jakobsen", Created: false);

        public int EnsureSuperadminCalls { get; private set; }
        public int DeleteCalls { get; private set; }

        public Task<CreateEntraUserResult> EnsureSuperadminAsync(
            string email,
            string displayName,
            CancellationToken cancellationToken)
        {
            EnsureSuperadminCalls++;
            Assert.Equal("rasmusvm6@hotmail.com", email);
            Assert.Equal("Rasmus Bak Jakobsen", displayName);
            return Task.FromResult(Result);
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
