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
            var tenantId = configuration["GraphApp:TenantId"];
            var clientId = configuration["GraphApp:ClientId"];
            var clientSecret = configuration["GraphApp:ClientSecret"];

            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            return new GraphServiceClient(credential, ["https://graph.microsoft.com/.default"]);
        });

        return builder;
    }
}
