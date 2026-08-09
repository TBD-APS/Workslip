using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Workslip.Api.Configuration;
using Xunit;

namespace Workslip.Tests.Configuration;

public sealed class LocalDevelopmentDatabaseMigrationTests
{
    [Theory]
    [InlineData("Server=localhost,1433;Database=WorkslipLocal;User Id=sa;Password=LocalOnly!123;TrustServerCertificate=true")]
    [InlineData("Server=127.0.0.1;Database=WorkslipLocal;Integrated Security=true;TrustServerCertificate=true")]
    [InlineData("Server=.;Database=WorkslipLocal;Integrated Security=true;TrustServerCertificate=true")]
    [InlineData("Server=(local);Database=WorkslipLocal;Integrated Security=true;TrustServerCertificate=true")]
    [InlineData("Server=(localdb)\\MSSQLLocalDB;Database=WorkslipLocal;Integrated Security=true;TrustServerCertificate=true")]
    [InlineData("Server=.\\SQLEXPRESS;Database=WorkslipLocal;Integrated Security=true;TrustServerCertificate=true")]
    [InlineData("Server=tcp:localhost,1433;Database=WorkslipLocal;User Id=sa;Password=LocalOnly!123;TrustServerCertificate=true")]
    public void ShouldApplyLocalMigrations_DevelopmentLocalSql_DefaultsToEnabled(string connectionString)
    {
        var environment = CreateEnvironment(Environments.Development);
        var configuration = BuildConfiguration(connectionString);

        Assert.True(DatabaseStartup.ShouldApplyLocalMigrations(environment, configuration));
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void ShouldApplyLocalMigrations_NonDevelopmentNeverApplies(string environmentName)
    {
        var environment = CreateEnvironment(environmentName);
        var configuration = BuildConfiguration(
            "Server=localhost;Database=WorkslipLocal;Integrated Security=true;TrustServerCertificate=true",
            applyLocalMigrations: true);

        Assert.False(DatabaseStartup.ShouldApplyLocalMigrations(environment, configuration));
    }

    [Fact]
    public void ShouldApplyLocalMigrations_DevelopmentRemoteSql_DefaultsToSkipped()
    {
        var environment = CreateEnvironment(Environments.Development);
        var configuration = BuildConfiguration(
            "Server=tcp:db-mrsoftware-prod-server.database.windows.net,1433;Initial Catalog=db-mrsoftware-prod;Authentication=Active Directory Default;Encrypt=true");

        Assert.False(DatabaseStartup.ShouldApplyLocalMigrations(environment, configuration));
    }

    [Fact]
    public void ShouldApplyLocalMigrations_DevelopmentRemoteSql_ExplicitEnableFailsClosed()
    {
        var environment = CreateEnvironment(Environments.Development);
        var configuration = BuildConfiguration(
            "Server=tcp:db-mrsoftware-prod-server.database.windows.net,1433;Initial Catalog=db-mrsoftware-prod;Authentication=Active Directory Default;Encrypt=true",
            applyLocalMigrations: true);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            DatabaseStartup.ShouldApplyLocalMigrations(environment, configuration));

        Assert.Contains(DatabaseStartup.ApplyLocalMigrationsKey, exception.Message, StringComparison.Ordinal);
        Assert.Contains("provably local SQL target", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ShouldApplyLocalMigrations_DevelopmentLocalSql_ExplicitDisableSkips()
    {
        var environment = CreateEnvironment(Environments.Development);
        var configuration = BuildConfiguration(
            "Server=localhost;Database=WorkslipLocal;Integrated Security=true;TrustServerCertificate=true",
            applyLocalMigrations: false);

        Assert.False(DatabaseStartup.ShouldApplyLocalMigrations(environment, configuration));
    }

    [Fact]
    public void ShouldApplyLocalMigrations_MissingConnectionStringSkips()
    {
        var environment = CreateEnvironment(Environments.Development);
        var configuration = BuildConfiguration(connectionString: null);

        Assert.False(DatabaseStartup.ShouldApplyLocalMigrations(environment, configuration));
    }

    [Fact]
    public async Task VerifyIfRequiredAsync_OpenApiModeDoesNotEvaluateRemoteMigrationTarget()
    {
        await using var services = new ServiceCollection()
            .BuildServiceProvider();
        var environment = CreateEnvironment(Environments.Development);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [DatabaseStartup.GenerateOpenApiOnlyKey] = bool.TrueString,
                [DatabaseStartup.ApplyLocalMigrationsKey] = bool.TrueString,
                ["Azure:Sql:ConnectionString"] =
                    "Server=tcp:db-mrsoftware-prod-server.database.windows.net,1433;Initial Catalog=db-mrsoftware-prod;Authentication=Active Directory Default;Encrypt=true"
            })
            .Build();

        await DatabaseStartup.VerifyIfRequiredAsync(
            services,
            configuration,
            seedDevelopmentData: false,
            seedDevelopmentEntraIdentities: false,
            environment);
    }

    private static IConfiguration BuildConfiguration(
        string? connectionString,
        bool? applyLocalMigrations = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Azure:Sql:ConnectionString"] = connectionString
        };

        if (applyLocalMigrations.HasValue)
            values[DatabaseStartup.ApplyLocalMigrationsKey] = applyLocalMigrations.Value.ToString();

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static IHostEnvironment CreateEnvironment(string name) =>
        new TestHostEnvironment
        {
            EnvironmentName = name,
            ContentRootPath = Path.GetTempPath()
        };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Workslip.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
