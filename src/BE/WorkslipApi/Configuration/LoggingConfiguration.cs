using Microsoft.ApplicationInsights.Extensibility;
using Serilog;
using Workslip.Api.Telemetry;

namespace Workslip.Api.Configuration;

public static class LoggingConfiguration
{
    public static WebApplicationBuilder ConfigureLogging(this WebApplicationBuilder builder, string? applicationInsightsConnectionString)
    {
        if (!string.IsNullOrWhiteSpace(applicationInsightsConnectionString))
        {
            builder.Services.AddApplicationInsightsTelemetry(options =>
            {
                options.ConnectionString = applicationInsightsConnectionString;
            });

            builder.Services.AddSingleton<ITelemetryInitializer, CorrelationTelemetryInitializer>();
        }

        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "Workslip.Api")
                .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
                .WriteTo.Console();

            if (context.HostingEnvironment.IsDevelopment())
            {
                configuration.WriteTo.Seq("http://localhost:5341");
            }

            if (!string.IsNullOrWhiteSpace(applicationInsightsConnectionString))
            {
                configuration.WriteTo.ApplicationInsights(
                    services.GetRequiredService<TelemetryConfiguration>(),
                    TelemetryConverter.Traces);
            }
        });

        return builder;
    }
}
