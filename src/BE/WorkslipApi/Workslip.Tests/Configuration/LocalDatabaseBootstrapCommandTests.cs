using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Workslip.Api.Configuration;
using Xunit;

namespace Workslip.Tests.Configuration;

public sealed class LocalDatabaseBootstrapCommandTests
{
    [Theory]
    [InlineData("bootstrap-local-db")]
    [InlineData(" BOOTSTRAP-LOCAL-DB ")]
    public void IsRequested_ExactOperationEnablesBootstrap(string operation)
    {
        Assert.True(LocalDatabaseBootstrapCommand.IsRequested(
            [$"--{WorkslipOperationParser.ConfigurationKey}={operation}"]));
    }

    [Fact]
    public void IsRequested_OtherOperationDoesNotEnableBootstrap()
    {
        Assert.False(LocalDatabaseBootstrapCommand.IsRequested(
            [$"--{WorkslipOperationParser.ConfigurationKey}=bootstrap-superadmins"]));
    }

    [Fact]
    public async Task ExecuteAsync_NonDevelopmentFailsBeforeResolvingDatabaseServices()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var environment = CreateEnvironment(Environments.Production);
        var configuration = BuildConfiguration(
            "Server=(localdb)\\MSSQLLocalDB;Database=WorkslipLocal;Integrated Security=true;TrustServerCertificate=true");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LocalDatabaseBootstrapCommand.ExecuteAsync(services, environment, configuration));

        Assert.Contains("Development-only", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_RemoteSqlFailsBeforeResolvingDatabaseServices()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var environment = CreateEnvironment(Environments.Development);
        var configuration = BuildConfiguration(
            "Server=tcp:db-mrsoftware-prod-server.database.windows.net,1433;Initial Catalog=db-mrsoftware-prod;Authentication=Active Directory Default;Encrypt=true");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LocalDatabaseBootstrapCommand.ExecuteAsync(services, environment, configuration));

        Assert.Contains("provably local SQL Server target", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_MissingConnectionStringFailsBeforeResolvingDatabaseServices()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        var environment = CreateEnvironment(Environments.Development);
        var configuration = BuildConfiguration(connectionString: null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            LocalDatabaseBootstrapCommand.ExecuteAsync(services, environment, configuration));

        Assert.Contains("Missing SQL connection string", exception.Message, StringComparison.Ordinal);
    }

    private static IConfiguration BuildConfiguration(string? connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Azure:Sql:ConnectionString"] = connectionString
            })
            .Build();

    private static IHostEnvironment CreateEnvironment(string environmentName) =>
        new TestHostEnvironment
        {
            EnvironmentName = environmentName,
            ContentRootPath = AppContext.BaseDirectory
        };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Workslip.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
