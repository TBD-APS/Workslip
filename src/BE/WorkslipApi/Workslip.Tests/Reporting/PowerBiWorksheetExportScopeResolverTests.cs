using Microsoft.EntityFrameworkCore;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Reporting;
using Workslip.Infrastructure.Schema;

namespace Workslip.Tests.Reporting;

public sealed class PowerBiWorksheetExportScopeResolverTests
{
    [Fact]
    public async Task ResolveOrganizationIdAsync_RequiresEmailAndEntraIdentityToMatchOneTenant()
    {
        await using var dbContext = CreateDbContext();
        var matchingOrganizationId = Guid.NewGuid();
        dbContext.Users.AddRange(
            User(matchingOrganizationId, "powerbi@example.com", "entra-reader"),
            User(Guid.NewGuid(), "powerbi@example.com", "another-entra-user"));
        await dbContext.SaveChangesAsync();
        var resolver = new PowerBiWorksheetExportScopeResolver(dbContext);

        var result = await resolver.ResolveOrganizationIdAsync(
            "powerbi@example.com",
            "entra-reader",
            CancellationToken.None);

        Assert.Equal(matchingOrganizationId, result);
        Assert.Null(await resolver.ResolveOrganizationIdAsync(
            "powerbi@example.com",
            "unknown-entra-user",
            CancellationToken.None));
    }

    [Fact]
    public async Task ResolveOrganizationIdAsync_RejectsAmbiguousCrossTenantIdentity()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.AddRange(
            User(Guid.NewGuid(), "powerbi@example.com", "entra-reader"),
            User(Guid.NewGuid(), "powerbi@example.com", "entra-reader"));
        await dbContext.SaveChangesAsync();
        var resolver = new PowerBiWorksheetExportScopeResolver(dbContext);

        var result = await resolver.ResolveOrganizationIdAsync(
            "powerbi@example.com",
            "entra-reader",
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveOrganizationIdAsync_RejectsMatchingNonAdminUser()
    {
        await using var dbContext = CreateDbContext();
        var organizationId = Guid.NewGuid();
        var user = User(organizationId, "powerbi@example.com", "entra-reader");
        user.Role = "User";
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        var resolver = new PowerBiWorksheetExportScopeResolver(dbContext);

        var result = await resolver.ResolveOrganizationIdAsync(
            "powerbi@example.com",
            "entra-reader",
            CancellationToken.None);

        Assert.Null(result);
    }

    private static SqlDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SqlDbContext(options);
    }

    private static UserDataRow User(Guid organizationId, string email, string entraId) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        DisplayName = "Power BI reader",
        Email = email,
        Phone = string.Empty,
        EntraEmail = email,
        EntraId = entraId,
        Role = "Admin",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
}
