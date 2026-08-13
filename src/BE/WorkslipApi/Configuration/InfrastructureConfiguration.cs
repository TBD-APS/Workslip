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
    private const string DefaultWindowsLocalDbConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Initial Catalog=WorkslipLocal;Integrated Security=true;TrustServerCertificate=true;MultipleActiveResultSets=true";

    public static WebApplicationBuilder ConfigureInfrastructure(this WebApplicationBuilder builder, string[] args)
    {
        var configuration = builder.Configuration;
        var platformBootstrapRequested = PlatformIdentityBootstrapCommand.IsRequested(args);

        Log.Information("[STARTUP 02.1] Configure Azure credential - START");
        var azureCredential = CreateAzureCredential(configuration);
        Log.Information(
            "[STARTUP 02.1] Configure Azure credential - OK ({CredentialMode})",
            string.IsNullOrWhiteSpace(configuration["Azure:ManagedIdentity:ClientId"])
                ? "DefaultAzureCredential"
                : "managed identity");

        AddAzureAppConfiguration(configuration, azureCredential);
        RestoreOperatorOverrides(
            builder.Environment,
            configuration,
            args,
            platformBootstrapRequested);
        EnforceDevelopmentSqlIsolation(
            builder.Environment,
            configuration,
            platformBootstrapRequested);

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
            Log.Warning(
                "[STARTUP 02.2] Load Azure App Configuration and Key Vault references - FAILED after {ElapsedMilliseconds} ms ({ExceptionType}). Check Azure authentication, RBAC and network access",
                stopwatch.ElapsedMilliseconds,
                exception.GetType().Name);
            throw;
        }
    }

    private static void RestoreOperatorOverrides(
        IHostEnvironment environment,
        ConfigurationManager configuration,
        string[] args,
        bool platformBootstrapRequested)
    {
        Log.Information("[STARTUP 02.3] Restore local operator configuration overrides - START");

        // Shared Azure configuration may be loaded before local overrides. Normal
        // Development startup deliberately reapplies the tracked safe baseline,
        // followed by machine-local settings, environment variables and command-line
        // values so a production SQL value from App Configuration cannot win by accident.
        // The explicit platform bootstrap operation is the only Development-mode path
        // that may intentionally target remote SQL, so it uses operator-supplied Azure
        // configuration plus environment/command-line overrides instead of local files.
        if (environment.IsDevelopment() && !platformBootstrapRequested)
        {
            configuration.AddJsonFile(
                "appsettings.Development.json",
                optional: true,
                reloadOnChange: true);
            configuration.AddJsonFile(
                "appsettings.Local.json",
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

    private static void EnforceDevelopmentSqlIsolation(
        IHostEnvironment environment,
        ConfigurationManager configuration,
        bool platformBootstrapRequested)
    {
        if (!environment.IsDevelopment())
        {
            Log.Information("[STARTUP 02.4] Enforce development SQL isolation - SKIPPED (non-development environment)");
            return;
        }

        if (DatabaseStartup.IsOpenApiGeneration(configuration))
        {
            Log.Information("[STARTUP 02.4] Enforce development SQL isolation - SKIPPED (OpenAPI generation mode)");
            return;
        }

        var connectionString = configuration[SqlConnectionStringKey];

        if (platformBootstrapRequested)
        {
            
            ConfigureExplicitOperatorSqlAuthentication(configuration, connectionString);
            Log.Information("[STARTUP 02.4] Enforce development SQL isolation - EXPLICIT OPERATOR EXCEPTION (bootstrap-superadmins)");
            return;
        }

        var isWindows = OperatingSystem.IsWindows();
        var resolvedConnectionString = ResolveDevelopmentConnectionString(
            connectionString,
            isWindows);

        if (!string.Equals(connectionString, resolvedConnectionString, StringComparison.Ordinal))
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [SqlConnectionStringKey] = resolvedConnectionString
            });

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Log.Information(
                    "[STARTUP 02.4] Enforce development SQL isolation - using default Windows LocalDB target ({DatabaseName})",
                    "WorkslipLocal");
            }
            else
            {
                Log.Warning(
                    "[STARTUP 02.4] Enforce development SQL isolation - ignored non-local/invalid configured SQL target and substituted default Windows LocalDB ({DatabaseName})",
                    "WorkslipLocal");
            }
        }

        if (!LocalDevelopmentDatabaseMigrationRunner.IsLocalSqlTarget(resolvedConnectionString))
        {
            throw new InvalidOperationException(
                "Development startup refused Azure:Sql:ConnectionString because the SQL target is not provably local. Use localhost/loopback, LocalDB, '.', or '(local)'. Remote/Azure SQL is allowed only for an explicit operator operation, not normal local startup.");
        }

        Log.Information("[STARTUP 02.4] Enforce development SQL isolation - OK (local SQL target verified)");
    }

    internal static string ResolveDevelopmentConnectionString(
        string? configuredConnectionString,
        bool isWindows)
    {
        if (!string.IsNullOrWhiteSpace(configuredConnectionString) &&
            LocalDevelopmentDatabaseMigrationRunner.IsLocalSqlTarget(configuredConnectionString))
        {
            return configuredConnectionString;
        }

        if (isWindows)
            return DefaultWindowsLocalDbConnectionString;

        if (string.IsNullOrWhiteSpace(configuredConnectionString))
        {
            throw new InvalidOperationException(
                "Development startup requires a local SQL connection string on this platform. Configure Azure:Sql:ConnectionString in appsettings.Local.json or an environment variable. Remote/Azure SQL is not allowed for normal Development startup.");
        }

        throw new InvalidOperationException(
            "Development startup refused Azure:Sql:ConnectionString because the SQL target is not provably local. Use localhost/loopback, LocalDB, '.', or '(local)'. Remote/Azure SQL is allowed only for an explicit operator operation, not normal local startup.");
    }

    private static void ConfigureExplicitOperatorSqlAuthentication(
        ConfigurationManager configuration,
        string connectionString)
    {
        SqlConnectionStringBuilder connectionStringBuilder;
        try
        {
            connectionStringBuilder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "The explicit operator SQL connection string could not be parsed.",
                exception);
        }

        if (connectionStringBuilder.Authentication != SqlAuthenticationMethod.ActiveDirectoryManagedIdentity)
            return;

        // A developer workstation cannot use the App Service managed identity. The
        // explicit bootstrap operation therefore preserves the remote target but uses
        // the authenticated developer Azure identity instead.
        connectionStringBuilder.Authentication = SqlAuthenticationMethod.ActiveDirectoryDefault;
        connectionStringBuilder.Remove("User ID");

        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [SqlConnectionStringKey] = connectionStringBuilder.ConnectionString
        });
    }
}
