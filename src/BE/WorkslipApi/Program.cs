using Microsoft.ApplicationInsights.Extensibility;
using Serilog;
using Serilog.Sinks.ApplicationInsights.TelemetryConverters;
using Workslip.Application;
using Workslip.Api.Endpoints;
using Workslip.Api.Middleware;
using Workslip.Infrastructure;
using Workslip.Infrastructure.Configuration;
using Workslip.Infrastructure.Schema;
using Scalar.AspNetCore;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    var applicationInsightsConnectionString = ResolveApplicationInsightsConnectionString(builder.Configuration);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "Workslip.Api")
        .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
        .WriteTo.ApplicationInsights(
            services.GetRequiredService<TelemetryConfiguration>(),
            TelemetryConverter.Traces));

    builder.Services.AddApplicationInsightsTelemetry(options =>
    {
        if (!string.IsNullOrWhiteSpace(applicationInsightsConnectionString))
        {
            options.ConnectionString = applicationInsightsConnectionString;
        }
    });
    builder.Services.AddOpenApi();
    builder.Services.AddWorkslipApplication();
    builder.Services.AddWorkslipInfrastructure();

    var app = builder.Build();

    await using (var scope = app.Services.CreateAsyncScope())
    {
        await scope.ServiceProvider.GetRequiredService<WorkslipSchemaRunner>().ApplyAsync(CancellationToken.None);
    }

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference();
    }

    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("TraceId", httpContext.TraceIdentifier);
            diagnosticContext.Set("RequestId", httpContext.Request.Headers.TryGetValue("X-Request-ID", out var requestId) ? requestId.ToString() : httpContext.TraceIdentifier);
            diagnosticContext.Set("Host", httpContext.Request.Host.Value);
            diagnosticContext.Set("Scheme", httpContext.Request.Scheme);
            diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString());
            diagnosticContext.Set("UserAgent", httpContext.Request.Headers.UserAgent.ToString());
            diagnosticContext.Set("Endpoint", httpContext.GetEndpoint()?.DisplayName);
            diagnosticContext.Set("QueryKeys", string.Join(",", httpContext.Request.Query.Keys));
        };
    });
    app.UseMiddleware<GlobalExceptionMiddleware>();

    app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
    app.MapOrganizationEndpoints();
    app.MapAuthEndpoints();
    app.MapJobEndpoints();

    await app.RunAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}

static string? ResolveApplicationInsightsConnectionString(IConfiguration configuration) =>
    ConfiguredValues.FirstConfigured(
        configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"],
        configuration["ApplicationInsights:ConnectionString"]);
