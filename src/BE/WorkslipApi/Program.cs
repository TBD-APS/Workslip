using Microsoft.EntityFrameworkCore;
using Serilog;
using Workslip.Api.Configuration;
using Workslip.Infrastructure.Schema;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.ConfigureInfrastructure();

    var applicationInsightsConnectionString = builder.Configuration["Azure:ApplicationInsights:ConnectionString"];

    builder.Services.AddCors(x =>
    {
        x.AddPolicy("Frontend", policy =>
        {
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                                 ?? new[] { "https://workslip-v2-0.vercel.app", "http://localhost:5270", "http://localhost:4173"};

            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        });
    });

    builder.ConfigureAuthentication();
    builder.ConfigureLogging(applicationInsightsConnectionString);
    builder.ConfigureServices();

    var app = builder.Build();

    await using (var scope = app.Services.CreateAsyncScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<SqlDbContext>();

        await db.Database.EnsureCreatedAsync();
        await db.Database.MigrateAsync();
        await db.Database.CanConnectAsync();

        if(app.Environment.IsDevelopment())
            await DatabaseSeeder.Seed(db);
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
