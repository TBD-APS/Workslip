using Azure.Identity;
using Microsoft.Graph;
using Workslip.Api.Services;
using Workslip.Application;
using Workslip.Infrastructure;

namespace Workslip.Api.Configuration;

public static class ServiceConfiguration
{
    public static WebApplicationBuilder ConfigureServices(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;

        builder.Services.AddOpenApi();
        builder.Services.AddHybridCache();

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<ICorrelationIdAccessor, CorrelationIdAccessor>();

        builder.Services.AddWorkslipApplication();
        builder.Services.AddWorkslipInfrastructure();

        builder.Services.AddSingleton<IJobReportPdfService, JobReportPdfService>();

        builder.Services.AddSingleton<GraphServiceClient>(sp =>
        {
            var managedIdentityClientId = configuration["Azure:ManagedIdentity:ClientId"]; 

            if(managedIdentityClientId == null)
                return new GraphServiceClient(new DefaultAzureCredential());

            var credential = new ManagedIdentityCredential(managedIdentityClientId);
            return new GraphServiceClient(credential, ["https://graph.microsoft.com/.default"]);
        });

        return builder;
    }
}
