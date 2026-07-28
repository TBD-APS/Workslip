using Azure.Core;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using QuestPDF.Infrastructure;

namespace Workslip.Api.Configuration;

public static class InfrastructureConfiguration
{
    private const string SqlConnectionStringKey = "Azure:Sql:ConnectionString";

    public static WebApplicationBuilder ConfigureInfrastructure(this WebApplicationBuilder builder, string[] args)
    {
        var configuration = builder.Configuration;
        var azureCredential = CreateAzureCredential(configuration);
        AddAzureAppConfiguration(configuration, azureCredential);
        RestoreOperatorOverrides(builder.Environment, configuration, args);
        ConfigureDevelopmentSqlAuthentication(builder.Environment, configuration);

        builder.Services.AddSingleton<TokenCredential>(azureCredential);

        QuestPDF.Settings.License = LicenseType.Community;

        return builder;
    }

    private static TokenCredential CreateAzureCredential(IConfiguration configuration)
    {
        var managedIdentityClientId = configuration["Azure:ManagedIdentity:ClientId"];

        if (string.IsNullOrWhiteSpace(managedIdentityClientId))
            return new DefaultAzureCredential();

        return new DefaultAzureCredential(new DefaultAzureCredentialOptions
        {
            ManagedIdentityClientId = managedIdentityClientId
        });
    }

    private static void AddAzureAppConfiguration(ConfigurationManager configuration, TokenCredential credential)
    {
        var endpoint = configuration["Azure:AppConfiguration:Endpoint"];

        if (string.IsNullOrWhiteSpace(endpoint))
            return;

        configuration.AddAzureAppConfiguration(options => options
            .Connect(new Uri(endpoint), credential)
            .ConfigureKeyVault(keyVault => keyVault.SetCredential(credential)));
    }

    private static void RestoreOperatorOverrides(
        IHostEnvironment environment,
        ConfigurationManager configuration,
        string[] args)
    {
        // Azure App Configuration supplies shared environment defaults. Local
        // development settings, environment variables and command-line values
        // retain the normal ASP.NET Core higher-precedence override behavior.
        if (environment.IsDevelopment())
        {
            configuration.AddJsonFile(
                "appsettings.Development.json",
                optional: true,
                reloadOnChange: true);
        }

        configuration.AddEnvironmentVariables();

        if (args.Length > 0)
            configuration.AddCommandLine(args);
    }

    private static void ConfigureDevelopmentSqlAuthentication(
        IHostEnvironment environment,
        ConfigurationManager configuration)
    {
        if (!environment.IsDevelopment())
            return;

        var connectionString = configuration[SqlConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        SqlConnectionStringBuilder connectionStringBuilder;
        try
        {
            connectionStringBuilder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            return;
        }

        if (connectionStringBuilder.Authentication != SqlAuthenticationMethod.ActiveDirectoryManagedIdentity)
            return;

        // App Configuration and Key Vault still own the server/database connection
        // details. Only the Azure-host-only authentication mode is adapted locally.
        // Active Directory Default uses the developer's Azure CLI/Visual Studio
        // identity and does not require a local SQL connection string or password.
        connectionStringBuilder.Authentication = SqlAuthenticationMethod.ActiveDirectoryDefault;
        connectionStringBuilder.Remove("User ID");

        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [SqlConnectionStringKey] = connectionStringBuilder.ConnectionString
        });
    }
}
