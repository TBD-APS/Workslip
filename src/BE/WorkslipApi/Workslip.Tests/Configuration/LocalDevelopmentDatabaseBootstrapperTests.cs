using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Workslip.Api.Configuration;
using Xunit;

namespace Workslip.Tests.Configuration;

public sealed class LocalDevelopmentDatabaseBootstrapperTests
{
    [Fact]
    public void ClassifySchemaState_MigrationHistoryOnly_IsFresh()
    {
        var state = LocalDevelopmentDatabaseBootstrapper.ClassifySchemaState(
            ["WorkslipSchemaMigrations"]);

        Assert.Equal(LocalDevelopmentSchemaState.Fresh, state);
    }

    [Fact]
    public void ClassifySchemaState_NoTables_IsFresh()
    {
        var state = LocalDevelopmentDatabaseBootstrapper.ClassifySchemaState([]);

        Assert.Equal(LocalDevelopmentSchemaState.Fresh, state);
    }

    [Fact]
    public void ClassifySchemaState_OrganizationsPresent_IsExisting()
    {
        var state = LocalDevelopmentDatabaseBootstrapper.ClassifySchemaState(
            ["Organizations", "Users"]);

        Assert.Equal(LocalDevelopmentSchemaState.Existing, state);
    }

    [Fact]
    public void ClassifySchemaState_PartialSchemaWithoutOrganizations_IsInconsistent()
    {
        var state = LocalDevelopmentDatabaseBootstrapper.ClassifySchemaState(
            ["WorkslipSchemaMigrations", "Users"]);

        Assert.Equal(LocalDevelopmentSchemaState.Inconsistent, state);
    }

    [Fact]
    public void ClassifySchemaState_UnrelatedTableWithoutOrganizations_IsInconsistent()
    {
        var state = LocalDevelopmentDatabaseBootstrapper.ClassifySchemaState(
            ["SomeUnexpectedTable"]);

        Assert.Equal(LocalDevelopmentSchemaState.Inconsistent, state);
    }

    [Fact]
    public async Task PrepareAsync_NonDevelopment_DoesNotResolveDatabaseServices()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();

        await LocalDevelopmentDatabasePreparation.PrepareAsync(
            services,
            BuildConfiguration(
                "Server=localhost,1433;Database=WorkslipLocal;User Id=sa;Password=LocalOnly!123;TrustServerCertificate=true"),
            CreateEnvironment(Environments.Production));
    }

    [Fact]
    public async Task PrepareAsync_RemoteDevelopmentSql_DoesNotResolveDatabaseServices()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();

        await LocalDevelopmentDatabasePreparation.PrepareAsync(
            services,
            BuildConfiguration(
                "Server=tcp:db-mrsoftware-prod-server.database.windows.net,1433;Initial Catalog=db-mrsoftware-prod;Authentication=Active Directory Default;Encrypt=true"),
            CreateEnvironment(Environments.Development));
    }

    private static IConfiguration BuildConfiguration(string connectionString) =>
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
