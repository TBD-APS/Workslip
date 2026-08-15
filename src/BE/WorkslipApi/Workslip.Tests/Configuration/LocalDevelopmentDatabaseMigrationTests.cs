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
    [InlineData("db")]
    [InlineData("sql.internal.example.com")]
    public void IsLocalDataSource_UnknownHost_DefaultsToNotLocal(string host)
    {
        Assert.False(LocalDevelopmentDatabaseMigrationRunner.IsLocalDataSource(host));
    }

    [Fact]
    public void IsLocalDataSource_AllowlistedHost_RequiresExplicitEnvironmentOptIn()
    {
        var variable = LocalDevelopmentDatabaseMigrationRunner.AdditionalLocalHostsVariable;
        var original = Environment.GetEnvironmentVariable(variable);
        try
        {
            Environment.SetEnvironmentVariable(variable, "db, other-host");

            Assert.True(LocalDevelopmentDatabaseMigrationRunner.IsLocalDataSource("db"));
            Assert.True(LocalDevelopmentDatabaseMigrationRunner.IsLocalDataSource("DB,1433"));
            Assert.True(LocalDevelopmentDatabaseMigrationRunner.IsLocalDataSource("other-host"));
            Assert.False(LocalDevelopmentDatabaseMigrationRunner.IsLocalDataSource("db-prod"));
            Assert.False(LocalDevelopmentDatabaseMigrationRunner.IsLocalDataSource("sql.example.com"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
        }
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

    [Fact]
    public void CreatedTableNames_ExtractsSchemaQualifiedTables()
    {
        const string sql = """
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;
            CREATE TABLE dbo.UserBillingRates
            (
                OrganizationId uniqueidentifier NOT NULL
            );
            CREATE TABLE dbo.WorksheetBillingSnapshots
            (
                OrganizationId uniqueidentifier NOT NULL
            );
            COMMIT TRANSACTION;
            """;

        var tables = LocalDevelopmentDatabaseMigrationRunner.CreatedTableNames(sql);

        Assert.Equal(
            new[] { "UserBillingRates", "WorksheetBillingSnapshots" },
            tables);
    }

    [Fact]
    public void CreatedTableNames_ExtractsGuardedCreateTable()
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.KnowledgeDocuments', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.KnowledgeDocuments
                (
                    Id uniqueidentifier NOT NULL
                );
            END;
            """;

        Assert.Equal(
            new[] { "KnowledgeDocuments" },
            LocalDevelopmentDatabaseMigrationRunner.CreatedTableNames(sql));
    }

    [Fact]
    public void CreatedTableNames_IgnoresTriggerAndIndexAndColumnStatements()
    {
        const string sql = """
            ALTER TABLE dbo.JobReports ADD IsInAuditorScope bit NOT NULL;
            CREATE INDEX IX_JobReports_Filial ON dbo.JobReports (FilialId);
            CREATE OR ALTER TRIGGER dbo.TR_Example ON dbo.Users AFTER INSERT AS BEGIN SET NOCOUNT ON; END;
            """;

        Assert.Empty(LocalDevelopmentDatabaseMigrationRunner.CreatedTableNames(sql));
    }

    [Fact]
    public void CreatesTableMissingFromSchema_TrueWhenCreatedTableAbsent()
    {
        const string sql = "CREATE TABLE dbo.UserBillingRates (OrganizationId uniqueidentifier NOT NULL);";
        var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Users",
            "Worksheets"
        };

        Assert.True(
            LocalDevelopmentDatabaseMigrationRunner.CreatesTableMissingFromSchema(sql, existingTables));
    }

    [Fact]
    public void CreatesTableMissingFromSchema_FalseWhenCreatedTableAlreadyModeled()
    {
        const string sql = """
            IF OBJECT_ID(N'dbo.OrganizationFilials', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.OrganizationFilials (Id uniqueidentifier NOT NULL);
            END;
            ALTER TABLE dbo.Users ADD FilialId uniqueidentifier NULL;
            """;
        var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Users",
            "OrganizationFilials"
        };

        Assert.False(
            LocalDevelopmentDatabaseMigrationRunner.CreatesTableMissingFromSchema(sql, existingTables));
    }

    [Fact]
    public void CreatesTableMissingFromSchema_FalseWhenMigrationCreatesNoTable()
    {
        const string sql = "ALTER TABLE dbo.JobReports ADD AuditorScopeReason nvarchar(500) NULL;";
        var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "JobReports" };

        Assert.False(
            LocalDevelopmentDatabaseMigrationRunner.CreatesTableMissingFromSchema(sql, existingTables));
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
