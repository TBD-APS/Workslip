using Microsoft.EntityFrameworkCore;
using Serilog;
using Workslip.Api.Configuration;
using Workslip.Infrastructure.Schema;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    var applicationInsightsConnectionString = builder.Configuration["Azure:ApplicationInsights:ConnectionString"];

    builder.ConfigureInfrastructure();
    builder.ConfigureLogging(applicationInsightsConnectionString);
    builder.ConfigureServices();
    builder.ConfigureAuthentication();

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

            await db.Database.EnsureCreatedAsync();
            await db.Database.MigrateAsync();
            await db.Database.CanConnectAsync();
            await DatabaseSeeder.Seed(db);
        }
    }

    app.ConfigurePipeline();
    app.ConfigureEndpoints();
    app.ConfigureDevEnvironment();

    await app.RunAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}
