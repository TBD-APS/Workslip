using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Workslip.Api.Configuration;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Configuration;

public sealed class PlatformIdentityBootstrapCommandTests
{
    [Theory]
    [InlineData("bootstrap-superadmins")]
    [InlineData(" BOOTSTRAP-SUPERADMINS ")]
    public void IsRequested_ExactOperationEnablesExplicitBootstrap(string operation)
    {
        Assert.True(PlatformIdentityBootstrapCommand.IsRequested(
            [$"--{PlatformIdentityBootstrapCommand.ConfigurationKey}={operation}"]));
    }

    [Fact]
    public void IsRequested_NormalStartupDoesNotEnableBootstrap()
    {
        Assert.False(PlatformIdentityBootstrapCommand.IsRequested([]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IsRequested_EmptyExplicitOperationFailsClosed(string operation)
    {
        Assert.Throws<InvalidOperationException>(() =>
            PlatformIdentityBootstrapCommand.IsRequested(
                [$"--{PlatformIdentityBootstrapCommand.ConfigurationKey}={operation}"]));
    }

    [Fact]
    public void IsRequested_UnknownOperationFailsClosed()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PlatformIdentityBootstrapCommand.IsRequested(
                [$"--{PlatformIdentityBootstrapCommand.ConfigurationKey}=seed-everything"]));

        Assert.Contains("Unsupported Workslip operation", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IsRequested_UnrelatedCommandLineConfigurationDoesNotEnableBootstrap()
    {
        Assert.False(PlatformIdentityBootstrapCommand.IsRequested(
            ["--ReleaseTesting:Enabled=true"]));
    }

    [Fact]
    public async Task ExecuteAsync_RunsOnlyPlatformIdentityBootstrapAgainstReadyDatabase()
    {
        var services = new ServiceCollection();
        var databaseName = Guid.NewGuid().ToString();
        services.AddLogging();
        services.AddDbContext<SqlDbContext>(options =>
            options.UseInMemoryDatabase(databaseName));
        services.AddSingleton<ISuperadminEntraService, FakeSuperadminEntraService>();
        services.AddScoped<PlatformIdentityBootstrapper>();
        await using var provider = services.BuildServiceProvider();

        await PlatformIdentityBootstrapCommand.ExecuteAsync(provider);

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();
        Assert.Single(await db.Organizations.ToListAsync());
        Assert.Equal(3, await db.Users.CountAsync());
        Assert.All(await db.Users.ToListAsync(), user =>
        {
            Assert.Equal(PlatformOrganization.Id, user.OrganizationId);
            Assert.Equal(Roles.Superadmin, user.Role);
        });
    }

    private sealed class FakeSuperadminEntraService : ISuperadminEntraService
    {
        public Task<CreateEntraUserResult> EnsureSuperadminAsync(
            string email,
            string displayName,
            CancellationToken cancellationToken)
        {
            var localPart = email.Split('@')[0];
            return Task.FromResult(new CreateEntraUserResult(
                $"entra-{localPart}",
                $"{localPart}#EXT#@tenant.onmicrosoft.com",
                displayName,
                Created: false));
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

        public Task DeleteUserAsync(string entraUserId, CancellationToken ct) =>
            Task.CompletedTask;
    }
}
