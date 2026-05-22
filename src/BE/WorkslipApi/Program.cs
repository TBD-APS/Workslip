using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Azure.Identity;
using Azure.Core;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Caching.Hybrid;
using Serilog;
using Serilog.Sinks.ApplicationInsights.TelemetryConverters;
using Workslip.Application;
using Workslip.Api.Endpoints;
using Workslip.Api.Middleware;
using Workslip.Infrastructure;
using Workslip.Infrastructure.Configuration;
using Workslip.Infrastructure.Schema;
using Scalar.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    var azureCredential = CreateAzureCredential(builder.Configuration);
    AddAzureAppConfiguration(builder.Configuration, azureCredential);
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
    
    builder.Services.AddHybridCache();
    builder.Services.AddWorkslipApplication();
    builder.Services.AddWorkslipInfrastructure();

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

    builder.Services.AddSingleton<IAuthorizationPolicyProvider, DynamicPolicyProvider>();
    builder.Services.AddSingleton<IAuthorizationHandler, DynamicRoleHandler>();

    var app = builder.Build();

    app.UseSecurityHeaders();
    
    app.UseMiddleware<GlobalExceptionMiddleware>();

    app.UseRateLimiter();
    app.UseSerilogRequestLogging();

    app.UseRouting();
    app.UseCors();

    app.UseAuthentication();
    app.UseAuthorization();

    await using (var scope = app.Services.CreateAsyncScope())
    {
        await scope.ServiceProvider.GetRequiredService<WorkslipSchemaRunner>().ApplyAsync(CancellationToken.None);
    }

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference(options =>
    {
        options.WithTitle("Workslip Konsulent API")
               .WithTheme(ScalarTheme.DeepSpace) // Vælg mellem fede temaer (Default, Purple, DeepSpace, Moonlight)
               .WithClassicLayout(); // Det professionelle 3-kolonne layout
    });
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
    
    app.MapGet("/health", (HttpContext httpContext) =>
    {
        HttpCacheHeaders.SetPublicHealthCache(httpContext);
        return Results.Ok(new { status = "ok" });
    });

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


static TokenCredential CreateAzureCredential(IConfiguration configuration)
{
    var managedIdentityClientId = ConfiguredValues.FirstConfigured(
        configuration["AZURE_CLIENT_ID"],
        configuration["Azure:ManagedIdentityClientId"]);

    if (string.IsNullOrWhiteSpace(managedIdentityClientId))
    {
        return new DefaultAzureCredential();
    }

    return new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ManagedIdentityClientId = managedIdentityClientId
    });
}

static void AddAzureAppConfiguration(ConfigurationManager configuration, TokenCredential credential)
{
    var endpoint = ConfiguredValues.FirstConfigured(
        configuration["AZURE_APP_CONFIG_ENDPOINT"],
        configuration["AzureAppConfiguration:Endpoint"]);

    if (string.IsNullOrWhiteSpace(endpoint))
    {
        return;
    }

    configuration.AddAzureAppConfiguration(options => options
        .Connect(new Uri(endpoint), credential)
        .ConfigureKeyVault(keyVault => keyVault.SetCredential(credential)));
}
