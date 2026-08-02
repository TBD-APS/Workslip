using Microsoft.EntityFrameworkCore;
using Serilog;
using Workslip.Api.Configuration;
using Workslip.Infrastructure.Schema;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.ConfigureInfrastructure(args);

    var applicationInsightsConnectionString = builder.Configuration["Azure:ApplicationInsights:ConnectionString"];

    builder.Services.AddCors(x =>
    {
        x.AddPolicy("Frontend", policy =>
        {
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                                 ?? new[]
                                 {
                                     "https://app.mrsoftware.dk",
                                     "http://localhost:5270"
                                 };

            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });

    builder.ConfigureAuthentication();
    builder.ConfigureLogging(applicationInsightsConnectionString);
    builder.ConfigureServices();

    var app = builder.Build();
    var releaseTestingEnabled = ReleaseTestingConfiguration.IsEnabled(
        app.Environment,
        app.Configuration);

    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

        await scope.ServiceProvider.GetRequiredService<DatabaseSchemaInitializer>().InitializeAsync();
        await db.Database.CanConnectAsync();

        if (releaseTestingEnabled)
        {
            await scope.ServiceProvider
                .GetRequiredService<DevelopmentDatabaseSeeder>()
                .SeedAsync();
        }
    }

    app.ConfigurePipeline();
    app.ConfigureEndpoints();
    app.ConfigureDevEnvironment(releaseTestingEnabled);

    await app.RunAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}
