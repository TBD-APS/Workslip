using Azure.Identity;
using Azure.Core;
using QuestPDF.Infrastructure;
using Serilog;
using Workslip.Api.Services;
using Workslip.Application;
using Workslip.Api;
using Workslip.Api.Endpoints;
using Workslip.Api.Middleware;
using Workslip.Infrastructure;
using Workslip.Infrastructure.Configuration;
using Workslip.Infrastructure.Schema;
using Scalar.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Graph;
using Microsoft.ApplicationInsights.Extensibility;
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();
try
{
    var builder = WebApplication.CreateBuilder(args);
    var azureCredential = CreateAzureCredential(builder.Configuration);
    AddAzureAppConfiguration(builder.Configuration, azureCredential);
    
    var applicationInsightsConnectionString = ResolveApplicationInsightsConnectionString(builder.Configuration);

    builder.Host.UseSerilog((context, services, configuration) =>
    {
        configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "Workslip.Api")
            .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
            .WriteTo.Seq("http://localhost:5341")
            .WriteTo.Console();

        if (!string.IsNullOrWhiteSpace(applicationInsightsConnectionString))
        {
            configuration.WriteTo.ApplicationInsights(
                services.GetRequiredService<TelemetryConfiguration>(),
                TelemetryConverter.Traces);
        }
    });
    
    builder.Services.AddOpenApi();

    builder.Services.AddHybridCache();
    builder.Services.AddSingleton<TokenCredential>(azureCredential);
    builder.Services.AddWorkslipApplication();
    builder.Services.AddWorkslipInfrastructure();

    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICorrelationIdAccessor, CorrelationIdAccessor>();

    QuestPDF.Settings.License = LicenseType.Community;
    builder.Services.AddSingleton<IJobReportPdfService, JobReportPdfService>();

    var tenantId = builder.Configuration["GraphApp:TenantId"];
    var clientId = builder.Configuration["GraphApp:ClientId"];
    var clientSecret = builder.Configuration["GraphApp:ClientSecret"];

    builder.Services.AddSingleton<GraphServiceClient>(sp =>
    {

        var credential = new ClientSecretCredential(
            tenantId,
            clientId,
            clientSecret
        );

        return new GraphServiceClient(
            credential,
            new[] { "https://graph.microsoft.com/.default" }
        );
    });

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

    builder.Services.AddAuthentication()
    .AddJwtBearer("LocalJwt", options =>
    {
        options.TokenValidationParameters = JwtHelper.GetTokenValidationParameters(builder.Configuration);
    });

    builder.Services.AddAuthorization();

    builder.Services.AddSingleton<IAuthorizationPolicyProvider, DynamicPolicyProvider>();
    builder.Services.AddSingleton<IAuthorizationHandler, DynamicRoleHandler>();

    var app = builder.Build();

    app.UseSecurityHeaders();
    
    app.UseMiddleware<GlobalExceptionMiddleware>();
    app.UseMiddleware<CorrelationIdMiddleware>();
    
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "{RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            diagnosticContext.Set("TraceId", httpContext.TraceIdentifier);
            diagnosticContext.Set("RequestId", httpContext.Request.Headers.TryGetValue("X-Request-ID", out var requestId) ? requestId.ToString() : httpContext.TraceIdentifier);
            diagnosticContext.Set("Host", httpContext.Request.Host.Value);
            diagnosticContext.Set("Endpoint", httpContext.GetEndpoint()?.DisplayName);
            diagnosticContext.Set("QueryKeys", string.Join(",", httpContext.Request.Query.Keys));
            diagnosticContext.Set("SourceContext", string.Empty);
        };
    });
    app.UseRouting();

    app.UseAuthentication();
    app.UseAuthorization();

    await using (var scope = app.Services.CreateAsyncScope())
    {
        await scope.ServiceProvider.GetRequiredService<WorkslipSchemaRunner>().ApplyAsync(CancellationToken.None);
    }

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.MapScalarApiReference();
    }
   
    app.MapGet("/health", (HttpContext httpContext) =>
    {
        HttpCacheHeaders.SetPublicHealthCache(httpContext);
        return Results.Ok(new { status = "ok" });
    });

    app.MapOrganizationEndpoints();
    app.MapAuthEndpoints();
    app.MapUserEndpoints();
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
