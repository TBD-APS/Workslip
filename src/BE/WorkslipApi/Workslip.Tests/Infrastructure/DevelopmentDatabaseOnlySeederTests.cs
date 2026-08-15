using Microsoft.EntityFrameworkCore;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Infrastructure;

public sealed class DevelopmentDatabaseOnlySeederTests
{
    private static readonly Guid LegacyArneId =
        new("B2B2B2B2-DA5B-4CC4-BBEB-07B40CAB806F");

    private const string LocalSuperadminEmail = "superadmin@17v3ygzs.mailosaur.net";
    private const string LegacyArneEmail = "user@17v3ygzs.mailosaur.net";

    [Fact]
    public async Task SeedAsync_CreatesSyntheticPlatformSuperadminAlongsideRegularDevUsers()
    {
        await using var context = CreateContext();

        await SeedAsync(context);

        var superadmin = await context.Users
            .AsNoTracking()
            .SingleAsync(user => user.Email == LocalSuperadminEmail);

        Assert.Equal(Roles.Superadmin, superadmin.Role);
        Assert.Equal(PlatformOrganization.Id, superadmin.OrganizationId);
        Assert.Equal(PlatformOrganization.Id, superadmin.FilialId);
        Assert.Equal("Local Superadmin", superadmin.DisplayName);
        Assert.True(string.IsNullOrWhiteSpace(superadmin.EntraId));
        Assert.True(string.IsNullOrWhiteSpace(superadmin.EntraEmail));

        var platformOrganization = await context.Organizations
            .AsNoTracking()
            .SingleAsync(organization => organization.Id == PlatformOrganization.Id);
        Assert.Equal(PlatformOrganization.Name, platformOrganization.Name);
        Assert.Equal(PlatformOrganization.Cvr, platformOrganization.Cvr);

        Assert.Equal(4, await context.Users.CountAsync());
        var tenantDevelopmentUsers = await context.Users
            .AsNoTracking()
            .Where(user => user.OrganizationId != PlatformOrganization.Id)
            .ToListAsync();
        Assert.Equal(3, tenantDevelopmentUsers.Count);
        Assert.All(
            tenantDevelopmentUsers,
            user => Assert.Equal(UserKinds.InternalTest, user.UserKind));
    }

    [Fact]
    public async Task SeedAsync_WhenRepeated_DoesNotDuplicateLocalSuperadmin()
    {
        await using var context = CreateContext();

        await SeedAsync(context);
        await SeedAsync(context);

        Assert.Single(await context.Users
            .AsNoTracking()
            .Where(user => user.Email == LocalSuperadminEmail)
            .ToListAsync());
        Assert.Equal(4, await context.Users.CountAsync());
        Assert.Equal(
            3,
            await context.Users.CountAsync(user =>
                user.OrganizationId != PlatformOrganization.Id
                && user.UserKind == UserKinds.InternalTest));
    }

    [Fact]
    public async Task SeedAsync_WhenLegacyArneIsMember_ReclassifiesExactIdentityAsInternalTest()
    {
        await using var context = CreateContext();
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var organization = CreateOrganization(now);
        context.Organizations.Add(organization);
        context.Users.Add(new UserDataRow
        {
            Id = LegacyArneId,
            OrganizationId = organization.Id,
            DisplayName = "Arne Arnesen",
            Email = LegacyArneEmail,
            Phone = "10000002",
            Role = Roles.User,
            UserKind = UserKinds.Member,
            CreatedAt = now,
            UpdatedAt = now
        });
        await context.SaveChangesAsync();

        await SeedAsync(context);

        var arne = await context.Users
            .AsNoTracking()
            .SingleAsync(user => user.Id == LegacyArneId);
        Assert.Equal(UserKinds.InternalTest, arne.UserKind);
        Assert.Equal(organization.Id, arne.OrganizationId);
        Assert.Equal(Roles.User, arne.Role);
    }

    [Fact]
    public async Task SeedAsync_WhenLegacyArneIdWasRepurposed_DoesNotReclassifyUser()
    {
        await using var context = CreateContext();
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var organization = CreateOrganization(now);
        context.Organizations.Add(organization);
        context.Users.Add(new UserDataRow
        {
            Id = LegacyArneId,
            OrganizationId = organization.Id,
            DisplayName = "Renamed employee",
            Email = "renamed@example.test",
            Phone = "10000002",
            Role = Roles.User,
            UserKind = UserKinds.Member,
            CreatedAt = now,
            UpdatedAt = now
        });
        await context.SaveChangesAsync();

        await SeedAsync(context);

        var preserved = await context.Users
            .AsNoTracking()
            .SingleAsync(user => user.Id == LegacyArneId);
        Assert.Equal(UserKinds.Member, preserved.UserKind);
        Assert.Equal("renamed@example.test", preserved.Email);
        Assert.Equal("Renamed employee", preserved.DisplayName);
    }

    [Fact]
    public async Task SeedAsync_WhenLocalSuperadminEmailBelongsToAnotherUser_FailsBeforeDemoSeed()
    {
        await using var context = CreateContext();
        var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var organization = CreateOrganization(now);
        context.Organizations.Add(organization);
        context.Users.Add(new UserDataRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            DisplayName = "Conflicting local identity",
            Email = LocalSuperadminEmail,
            Phone = string.Empty,
            Role = Roles.User,
            CreatedAt = now,
            UpdatedAt = now
        });
        await context.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => SeedAsync(context));

        Assert.Contains("ID/email conflict", exception.Message, StringComparison.Ordinal);
        Assert.Single(await context.Users.ToListAsync());
        Assert.Empty(await context.Customers.ToListAsync());
        Assert.Empty(await context.JobReports.ToListAsync());
    }

    private static Task SeedAsync(SqlDbContext context) =>
        DevelopmentDatabaseOnlySeeder.SeedAsync(
            context,
            new InstallationBaselineProvisioner(context));

    private static SqlDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new SqlDbContext(options);
    }

    private static OrganizationRow CreateOrganization(DateTimeOffset timestamp) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "Existing tenant",
            Cvr = "12345678",
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
}
