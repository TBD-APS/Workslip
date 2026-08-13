using Ardalis.Result;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Application.Auth;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Users;

public sealed class UserBillingServiceTests
{
    [Fact]
    public async Task UpdateAsync_rounds_rate_and_persists_inside_current_organization()
    {
        var organizationId = Guid.NewGuid();
        await using var context = CreateContext();
        var user = CreateUser(organizationId, Roles.User);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context, organizationId, Roles.Admin);

        var result = await service.UpdateAsync(
            user.Id,
            new UpdateBillableHourlyRateRequest(812.345m),
            CancellationToken.None);

        Assert.Equal(ResultStatus.NoContent, result.Status);
        context.ChangeTracker.Clear();
        var persisted = await context.Users.SingleAsync(candidate => candidate.Id == user.Id);
        Assert.Equal(812.35m, persisted.BillableHourlyRate);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100000.01)]
    public async Task UpdateAsync_rejects_out_of_range_rate(decimal rate)
    {
        var organizationId = Guid.NewGuid();
        await using var context = CreateContext();
        var user = CreateUser(organizationId, Roles.User);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context, organizationId, Roles.Admin);

        var result = await service.UpdateAsync(
            user.Id,
            new UpdateBillableHourlyRateRequest(rate),
            CancellationToken.None);

        Assert.Equal(ResultStatus.Invalid, result.Status);
        context.ChangeTracker.Clear();
        Assert.Null((await context.Users.SingleAsync(candidate => candidate.Id == user.Id)).BillableHourlyRate);
    }

    [Fact]
    public async Task GetAsync_does_not_cross_organization_boundary()
    {
        var currentOrganizationId = Guid.NewGuid();
        var otherOrganizationId = Guid.NewGuid();
        await using var context = CreateContext();
        var user = CreateUser(otherOrganizationId, Roles.User);
        user.BillableHourlyRate = 900m;
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context, currentOrganizationId, Roles.Admin);

        var result = await service.GetAsync(user.Id, CancellationToken.None);

        Assert.Equal(ResultStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task Admin_cannot_read_or_change_superadmin_rate()
    {
        var organizationId = Guid.NewGuid();
        await using var context = CreateContext();
        var user = CreateUser(organizationId, Roles.Superadmin);
        user.BillableHourlyRate = 1000m;
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var service = CreateService(context, organizationId, Roles.Admin);

        var read = await service.GetAsync(user.Id, CancellationToken.None);
        var update = await service.UpdateAsync(user.Id, new UpdateBillableHourlyRateRequest(1200m), CancellationToken.None);

        Assert.Equal(ResultStatus.Forbidden, read.Status);
        Assert.Equal(ResultStatus.Forbidden, update.Status);
        context.ChangeTracker.Clear();
        Assert.Equal(1000m, (await context.Users.SingleAsync(candidate => candidate.Id == user.Id)).BillableHourlyRate);
    }

    private static UserBillingService CreateService(SqlDbContext context, Guid organizationId, string role)
    {
        var currentUser = new TestCurrentUserContext(Guid.NewGuid(), organizationId, role);
        var repository = new EfUserRepository(context, currentUser);
        return new UserBillingService(repository, currentUser, NullLogger<UserBillingService>.Instance);
    }

    private static SqlDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SqlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new SqlDbContext(options);
    }

    private static UserDataRow CreateUser(Guid organizationId, string role) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        FilialId = Guid.NewGuid(),
        Email = $"{Guid.NewGuid():N}@example.test",
        DisplayName = "Rate Test",
        EntraId = Guid.NewGuid().ToString("N"),
        EntraEmail = $"{Guid.NewGuid():N}@example.test",
        Phone = string.Empty,
        Role = role,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private sealed class TestCurrentUserContext(Guid userId, Guid organizationId, string role) : ICurrentUserContext
    {
        public Guid? UserId { get; } = userId;
        public Guid? OrganizationId { get; } = organizationId;
        public string? Role { get; } = role;
    }
}
