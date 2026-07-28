using Azure.Core;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using QuestPDF.Infrastructure;

namespace Workslip.Api.Configuration;

public static class InfrastructureConfiguration
{
    public static WebApplicationBuilder ConfigureInfrastructure(this WebApplicationBuilder builder, string[] args)
    {
        var configuration = builder.Configuration;
        var azureCredential = CreateAzureCredential(configuration);
        AddAzureAppConfiguration(configuration, azureCredential);
        RestoreOperatorOverrides(builder.Environment, configuration, args);
        ValidateDevelopmentSqlConfiguration(builder.Environment, configuration);

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

    private static void ValidateDevelopmentSqlConfiguration(
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        if (!environment.IsDevelopment())
            return;

        var connectionString = configuration["Azure:Sql:ConnectionString"];
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

        throw new InvalidOperationException(
            "Development resolved Azure:Sql:ConnectionString to Active Directory Managed Identity, " +
            "which only works inside Azure. Configure a local connection string in " +
            "appsettings.Development.json or Azure__Sql__ConnectionString.");
    }
}
