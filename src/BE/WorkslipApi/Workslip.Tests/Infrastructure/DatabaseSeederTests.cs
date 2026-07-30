using Microsoft.EntityFrameworkCore;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Infrastructure;

public sealed class DatabaseSeederTests
{
    private static readonly IReadOnlyDictionary<string, ExpectedDevelopmentUser> ExpectedDevelopmentUsers =
        new Dictionary<string, ExpectedDevelopmentUser>(StringComparer.OrdinalIgnoreCase)
        {
            ["admin@17v3ygzs.mailosaur.net"] = new(
                new Guid("A1A1A1A1-DA5B-4CC4-BBEB-07B40CAB806F"),
                "Niels Petersen",
                "10000001",
                Roles.Admin),
            ["user@17v3ygzs.mailosaur.net"] = new(
                new Guid("B2B2B2B2-DA5B-4CC4-BBEB-07B40CAB806F"),
                "Arne Arnesen",
                "10000002",
                Roles.User),
            ["auditor@17v3ygzs.mailosaur.net"] = new(
                new Guid("C3C3C3C3-DA5B-4CC4-BBEB-07B40CAB806F"),
                "Auditor Jakobsen",
                "10000003",
                Roles.Auditor)
        };

    [Fact]
    public async Task Seed_with_partially_seeded_database_adds_missing_development_users_to_oldest_organization()
    {
        await using var context = CreateContext();
        var oldestOrganization = CreateOrganization(
            new Guid("11111111-1111-1111-1111-111111111111"),
            "Oldest organization",
            "12345678",
            DateTimeOffset.Parse("2025-01-01T00:00:00Z"));
        var newerOrganization = CreateOrganization(
            new Guid("22222222-2222-2222-2222-222222222222"),
            "Newer organization",
            "87654321",
            DateTimeOffset.Parse("2025-02-01T00:00:00Z"));
        var existingDevelopmentUser = CreateExistingRegularUser(oldestOrganization.Id);
        var unrelatedUser = new UserDataRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = newerOrganization.Id,
            DisplayName = "Existing User",
            Email = "existing@example.test",
            Phone = "12345678",
            Role = Roles.User,
            CreatedAt = DateTimeOffset.Parse("2025-02-02T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2025-02-02T00:00:00Z")
        };

        context.Organizations.AddRange(newerOrganization, oldestOrganization);
        context.Users.AddRange(existingDevelopmentUser, unrelatedUser);
        await context.SaveChangesAsync();

        await DatabaseSeeder.Seed(context);

        var developmentUsers = await context.Users
            .AsNoTracking()
            .Where(user => ExpectedDevelopmentUsers.Keys.Contains(user.Email))
            .ToListAsync();

        Assert.Equal(ExpectedDevelopmentUsers.Count, developmentUsers.Count);
        foreach (var user in developmentUsers)
        {
            var expected = ExpectedDevelopmentUsers[user.Email];
            Assert.Equal(expected.Id, user.Id);
            Assert.Equal(expected.DisplayName, user.DisplayName);
            Assert.Equal(expected.Phone, user.Phone);
            Assert.Equal(expected.Role, user.Role);
            Assert.Equal(oldestOrganization.Id, user.OrganizationId);
        }

        Assert.Equal(4, await context.Users.CountAsync());
        Assert.Equal(newerOrganization.Id, unrelatedUser.OrganizationId);
        Assert.False(context.IsSeeding);
    }

    [Fact]
    public async Task Seed_when_run_repeatedly_does_not_duplicate_or_change_development_users()
    {
        await using var context = CreateContext();
        var organization = CreateOrganization(
            Guid.NewGuid(),
            "Existing organization",
            "12345678",
            DateTimeOffset.Parse("2025-01-01T00:00:00Z"));
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        await DatabaseSeeder.Seed(context);
        var firstSeed = await GetDevelopmentUserSnapshot(context);

        await DatabaseSeeder.Seed(context);
        var secondSeed = await GetDevelopmentUserSnapshot(context);

        Assert.Equal(ExpectedDevelopmentUsers.Count, await context.Users.CountAsync());
        Assert.Equal(firstSeed, secondSeed);
        Assert.False(context.IsSeeding);
    }

    [Fact]
    public async Task Seed_when_platform_is_oldest_adds_ordinary_users_to_oldest_customer()
    {
        await using var context = CreateContext();
        var platform = CreateOrganization(
            PlatformOrganization.Id,
            PlatformOrganization.Name,
            PlatformOrganization.Cvr,
            DateTimeOffset.Parse("2024-01-01T00:00:00Z"));
        var customer = CreateOrganization(
            Guid.NewGuid(),
            "Existing customer",
            "12345678",
            DateTimeOffset.Parse("2025-01-01T00:00:00Z"));
        context.Organizations.AddRange(platform, customer);
        await context.SaveChangesAsync();

        await DatabaseSeeder.Seed(context);

        var developmentUsers = await context.Users.AsNoTracking().ToListAsync();
        Assert.Equal(ExpectedDevelopmentUsers.Count, developmentUsers.Count);
        Assert.All(developmentUsers, user => Assert.Equal(customer.Id, user.OrganizationId));
        Assert.DoesNotContain(developmentUsers, user => user.Role == Roles.Superadmin);
    }

    [Fact]
    public async Task Seed_when_canonical_id_has_different_email_preserves_existing_user()
    {
        await using var context = CreateContext();
        var organization = CreateOrganization(
            Guid.NewGuid(),
            "Existing organization",
            "12345678",
            DateTimeOffset.Parse("2025-01-01T00:00:00Z"));
        var canonicalAdmin = ExpectedDevelopmentUsers["admin@17v3ygzs.mailosaur.net"];
        var timestamp = DateTimeOffset.Parse("2025-01-02T00:00:00Z");
        var conflictingUser = new UserDataRow
        {
            Id = canonicalAdmin.Id,
            OrganizationId = organization.Id,
            DisplayName = "Renamed User",
            Email = "renamed@example.test",
            Phone = "87654321",
            Role = Roles.User,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
        context.Organizations.Add(organization);
        context.Users.Add(conflictingUser);
        await context.SaveChangesAsync();

        await DatabaseSeeder.Seed(context);

        var preserved = await context.Users.AsNoTracking().SingleAsync(user => user.Id == canonicalAdmin.Id);
        Assert.Equal("Renamed User", preserved.DisplayName);
        Assert.Equal("renamed@example.test", preserved.Email);
        Assert.Equal("87654321", preserved.Phone);
        Assert.Equal(Roles.User, preserved.Role);
        Assert.Equal(timestamp, preserved.CreatedAt);
        Assert.Equal(timestamp, preserved.UpdatedAt);
        Assert.False(await context.Users.AnyAsync(user => user.Email == "admin@17v3ygzs.mailosaur.net"));
        Assert.Equal(ExpectedDevelopmentUsers.Count, await context.Users.CountAsync());
    }

    [Fact]
    public async Task Seed_when_canonical_email_has_different_id_preserves_existing_user()
    {
        await using var context = CreateContext();
        var organization = CreateOrganization(
            Guid.NewGuid(),
            "Existing organization",
            "12345678",
            DateTimeOffset.Parse("2025-01-01T00:00:00Z"));
        var canonicalAuditor = ExpectedDevelopmentUsers["auditor@17v3ygzs.mailosaur.net"];
        var conflictingId = Guid.NewGuid();
        var timestamp = DateTimeOffset.Parse("2025-01-02T00:00:00Z");
        var conflictingUser = new UserDataRow
        {
            Id = conflictingId,
            OrganizationId = organization.Id,
            DisplayName = "Existing Auditor Email",
            Email = "auditor@17v3ygzs.mailosaur.net",
            Phone = "87654321",
            Role = Roles.Admin,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
        context.Organizations.Add(organization);
        context.Users.Add(conflictingUser);
        await context.SaveChangesAsync();

        await DatabaseSeeder.Seed(context);

        var preserved = await context.Users.AsNoTracking().SingleAsync(user => user.Id == conflictingId);
        Assert.Equal("Existing Auditor Email", preserved.DisplayName);
        Assert.Equal("87654321", preserved.Phone);
        Assert.Equal(Roles.Admin, preserved.Role);
        Assert.Equal(timestamp, preserved.CreatedAt);
        Assert.Equal(timestamp, preserved.UpdatedAt);
        Assert.False(await context.Users.AnyAsync(user => user.Id == canonicalAuditor.Id));
        Assert.Equal(ExpectedDevelopmentUsers.Count, await context.Users.CountAsync());
    }

    private static async Task<DevelopmentUserSnapshot[]> GetDevelopmentUserSnapshot(SqlDbContext context) =>
        await context.Users
            .AsNoTracking()
            .OrderBy(user => user.Email)
            .Select(user => new DevelopmentUserSnapshot(
                user.Id,
                user.OrganizationId,
                user.Email,
                user.DisplayName,
                user.Phone,
                user.Role,
                user.CreatedAt,
                user.UpdatedAt))
            .ToArrayAsync();

    private static SqlDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new SqlDbContext(options);
    }

    private static OrganizationRow CreateOrganization(
        Guid id,
        string name,
        string cvr,
        DateTimeOffset timestamp) =>
        new()
        {
            Id = id,
            Name = name,
            Cvr = cvr,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };

    private static UserDataRow CreateExistingRegularUser(Guid organizationId)
    {
        var expected = ExpectedDevelopmentUsers["user@17v3ygzs.mailosaur.net"];
        var timestamp = DateTimeOffset.Parse("2025-01-02T00:00:00Z");

        return new UserDataRow
        {
            Id = expected.Id,
            OrganizationId = organizationId,
            DisplayName = expected.DisplayName,
            Email = "user@17v3ygzs.mailosaur.net",
            Phone = expected.Phone,
            Role = expected.Role,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    private sealed record ExpectedDevelopmentUser(Guid Id, string DisplayName, string Phone, string Role);

    private sealed record DevelopmentUserSnapshot(
        Guid Id,
        Guid OrganizationId,
        string Email,
        string DisplayName,
        string Phone,
        string Role,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);
}
