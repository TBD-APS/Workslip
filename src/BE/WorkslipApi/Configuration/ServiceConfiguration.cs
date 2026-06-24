using System.Text.Json.Serialization;
using Azure.Core;
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
        builder.Services.AddMemoryCache();

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<ICorrelationIdAccessor, CorrelationIdAccessor>();

        builder.Services.AddWorkslipApplication();
        builder.Services.AddWorkslipInfrastructure();

        builder.Services.AddSingleton<IJobReportPdfService, JobReportPdfService>();

        builder.Services.AddSingleton(sp =>
        {
            var credential = sp.GetRequiredService<TokenCredential>();
            return new GraphServiceClient(credential, ["https://graph.microsoft.com/.default"]);
        });

        return builder;
    }
}
