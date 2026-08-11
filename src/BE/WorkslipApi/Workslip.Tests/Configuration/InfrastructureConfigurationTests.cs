using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Workslip.Api.Configuration;
using Xunit;

namespace Workslip.Tests.Configuration;

public sealed class InfrastructureConfigurationTests
{
    private const string SqlConnectionStringKey = "Azure:Sql:ConnectionString";
    private const string ManagedIdentityClientId = "11111111-2222-3333-4444-555555555555";

    [Fact]
    public void ConfigureInfrastructure_InDevelopment_RejectsRemoteSqlForNormalStartup()
    {
        var args = new[] { $"--{SqlConnectionStringKey}={CreateManagedIdentityConnectionString()}" };
        var builder = CreateBuilder(Environments.Development, args);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.ConfigureInfrastructure(args));

        Assert.Contains("not provably local", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfigureInfrastructure_InDevelopment_AllowsLocalSql()
    {
        var localConnectionString = CreateLocalConnectionString();
        var args = new[] { $"--{SqlConnectionStringKey}={localConnectionString}" };
        var builder = CreateBuilder(Environments.Development, args);

        builder.ConfigureInfrastructure(args);

        var connectionString = new SqlConnectionStringBuilder(builder.Configuration[SqlConnectionStringKey]);
        Assert.Equal("localhost,1433", connectionString.DataSource);
        Assert.Equal("WorkslipLocal", connectionString.InitialCatalog);
    }

    [Fact]
    public void ConfigureInfrastructure_ExplicitBootstrapInDevelopment_AllowsRemoteSqlWithDeveloperAzureIdentity()
    {
        var args = new[]
        {
            $"--{PlatformIdentityBootstrapCommand.ConfigurationKey}={PlatformIdentityBootstrapCommand.OperationName}",
            $"--{SqlConnectionStringKey}={CreateManagedIdentityConnectionString()}"
        };
        var builder = CreateBuilder(Environments.Development, args);

        builder.ConfigureInfrastructure(args);

        var connectionString = new SqlConnectionStringBuilder(builder.Configuration[SqlConnectionStringKey]);
        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryDefault, connectionString.Authentication);
        Assert.True(string.IsNullOrWhiteSpace(connectionString.UserID));
        Assert.Equal("tcp:db-mrsoftware-prod-server.database.windows.net,1433", connectionString.DataSource);
        Assert.Equal("db-mrsoftware-prod", connectionString.InitialCatalog);
    }

    [Fact]
    public void ConfigureInfrastructure_InProduction_PreservesManagedIdentityAuthentication()
    {
        var builder = CreateBuilder(Environments.Production);
        builder.Configuration[SqlConnectionStringKey] = CreateManagedIdentityConnectionString();

        builder.ConfigureInfrastructure([]);

        var connectionString = new SqlConnectionStringBuilder(builder.Configuration[SqlConnectionStringKey]);
        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryManagedIdentity, connectionString.Authentication);
        Assert.Equal(ManagedIdentityClientId, connectionString.UserID);
    }

    [Fact]
    public void ConfigureInfrastructure_RestoresCommandLineAsHighestOperatorOverride()
    {
        const string key = "Workslip:StartupDiagnosticsProbe";
        var args = new[]
        {
            $"--{key}=command-line",
            $"--{SqlConnectionStringKey}={CreateLocalConnectionString()}"
        };
        var builder = CreateBuilder(Environments.Development, args);

        // Simulate a provider added after the default WebApplication configuration,
        // as Azure App Configuration is during ConfigureInfrastructure.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [key] = "remote-configuration"
        });

        builder.ConfigureInfrastructure(args);

        Assert.Equal("command-line", builder.Configuration[key]);
    }

    private static WebApplicationBuilder CreateBuilder(string environmentName, string[]? args = null) =>
        WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName,
            Args = args ?? []
        });

    private static string CreateLocalConnectionString() =>
        "Server=localhost,1433;" +
        "Initial Catalog=WorkslipLocal;" +
        "User Id=workslip-local-test;" +
        "Password=not-a-real-secret;" +
        "Encrypt=False;TrustServerCertificate=True;Connection Timeout=5;";

    private static string CreateManagedIdentityConnectionString() =>
        $"Server=tcp:db-mrsoftware-prod-server.database.windows.net,1433;" +
        "Initial Catalog=db-mrsoftware-prod;" +
        "Authentication=Active Directory Managed Identity;" +
        $"User Id={ManagedIdentityClientId};" +
        "Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";
}
