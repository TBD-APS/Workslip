using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Azure.Core;
using Microsoft.ApplicationInsights;
using Microsoft.Graph;
using Workslip.Api.Endpoints;
using Workslip.Api.Services;
using Workslip.Api.Telemetry;
using Workslip.Application;
using Workslip.Application.Common;
using Workslip.Infrastructure;

namespace Workslip.Api.Configuration;

public static class ServiceConfiguration
{
    public static WebApplicationBuilder ConfigureServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddOpenApi();
        builder.Services.AddHybridCache();
        builder.Services.AddMemoryCache();
        builder.Services.AddHttpClient("vercel-cache");
        builder.Services.AddSingleton(_ => new CacheDiagnostics(
        [
            new CacheRegionDefinition(CacheRegionNames.ReferenceData, "HybridCache", 600),
            new CacheRegionDefinition(CacheRegionNames.AuthenticatedUsers, "IMemoryCache", 3600)
        ]));
        builder.Services.AddSingleton<ICacheDiagnostics>(services => new TelemetryCacheDiagnostics(
            services.GetRequiredService<CacheDiagnostics>(),
            services.GetService<TelemetryClient>()));
        builder.Services.AddScoped<IdempotencyStore>();
        builder.Services.AddScoped<IdempotentMutationService>();
        builder.Services.AddSingleton<ICustomerImportFormatParser, CustomerCsvParser>();
        builder.Services.AddSingleton<ICustomerImportFormatParser, CustomerExcelParser>();
        builder.Services.AddSingleton<CustomerImportFileParser>();

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<ICorrelationIdAccessor, CorrelationIdAccessor>();

        builder.Services.AddWorkslipApplication();
        builder.Services.AddWorkslipInfrastructure(
            includeHostedServices: !DatabaseStartup.IsOpenApiGeneration(builder.Configuration));

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy("customer-import", httpContext =>
            {
                var partitionKey = httpContext.User.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    QueueLimit = 0,
                    Window = TimeSpan.FromMinutes(1)
                });
            });

            options.AddPolicy("diagnostics-read", httpContext =>
            {
                var partitionKey = httpContext.User.Identity?.Name
                    ?? httpContext.User.FindFirst("sub")?.Value
                    ?? httpContext.Connection.RemoteIpAddress?.ToString()
                    ?? "anonymous";

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    QueueLimit = 0,
                    Window = TimeSpan.FromMinutes(1)
                });
            });
        });

        builder.Services.AddSingleton<IJobReportPdfService, JobReportPdfService>();

        builder.Services.AddSingleton(sp =>
        {
            var credential = sp.GetRequiredService<TokenCredential>();
            return new GraphServiceClient(credential, ["https://graph.microsoft.com/.default"]);
        });

        return builder;
    }
}
