using System.Diagnostics;
using Azure.Core;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using QuestPDF.Infrastructure;
using Serilog;

namespace Workslip.Api.Configuration;

public static class InfrastructureConfiguration
{
    private const string SqlConnectionStringKey = "Azure:Sql:ConnectionString";

    public static WebApplicationBuilder ConfigureInfrastructure(this WebApplicationBuilder builder, string[] args)
    {
        var configuration = builder.Configuration;

        Log.Information("[STARTUP 02.1] Configure Azure credential - START");
        var azureCredential = CreateAzureCredential(configuration);
        Log.Information(
            "[STARTUP 02.1] Configure Azure credential - OK ({CredentialMode})",
            string.IsNullOrWhiteSpace(configuration["Azure:ManagedIdentity:ClientId"])
                ? "DefaultAzureCredential"
                : "managed identity");

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
        {
            Log.Information("[STARTUP 02.2] Load Azure App Configuration and Key Vault references - SKIPPED (not configured)");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        Log.Information("[STARTUP 02.2] Load Azure App Configuration and Key Vault references - START");

        try
        {
            configuration.AddAzureAppConfiguration(options => options
                .Connect(new Uri(endpoint), credential)
                .ConfigureKeyVault(keyVault => keyVault.SetCredential(credential)));

            Log.Information(
                "[STARTUP 02.2] Load Azure App Configuration and Key Vault references - OK ({ElapsedMilliseconds} ms)",
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception)
        {
            Log.Error(
                exception,
                "[STARTUP 02.2] Load Azure App Configuration and Key Vault references - FAILED after {ElapsedMilliseconds} ms. Check Azure authentication, RBAC and network access",
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private static void RestoreOperatorOverrides(
        IHostEnvironment environment,
        ConfigurationManager configuration,
        string[] args)
    {
        Log.Information("[STARTUP 02.3] Restore local operator configuration overrides - START");

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
        {
            configuration.AddCommandLine(args);
        }

        Log.Information("[STARTUP 02.3] Restore local operator configuration overrides - OK");
    }

    private static void ConfigureDevelopmentSqlAuthentication(
        IHostEnvironment environment,
        ConfigurationManager configuration)
    {
        if (!environment.IsDevelopment())
        {
            Log.Information("[STARTUP 02.4] Configure development SQL authentication - SKIPPED (non-development environment)");
            return;
        }

        var connectionString = configuration[SqlConnectionStringKey];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Log.Information("[STARTUP 02.4] Configure development SQL authentication - SKIPPED (SQL connection is not configured)");
            return;
        }

        SqlConnectionStringBuilder connectionStringBuilder;
        try
        {
            connectionStringBuilder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            Log.Warning("[STARTUP 02.4] Configure development SQL authentication - SKIPPED (SQL connection configuration could not be parsed)");
            return;
        }

        if (connectionStringBuilder.Authentication != SqlAuthenticationMethod.ActiveDirectoryManagedIdentity)
        {
            Log.Information("[STARTUP 02.4] Configure development SQL authentication - OK (no local adaptation required)");
            return;
        }

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

        Log.Information("[STARTUP 02.4] Configure development SQL authentication - OK (developer Azure identity enabled)");
    }
}
