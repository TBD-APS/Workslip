using Azure.Core;
using Azure.Identity;
using QuestPDF.Infrastructure;

namespace Workslip.Api.Configuration;

public static class InfrastructureConfiguration
{
    public static WebApplicationBuilder ConfigureInfrastructure(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var azureCredential = CreateAzureCredential(configuration);
        AddAzureAppConfiguration(configuration, azureCredential);

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
}
