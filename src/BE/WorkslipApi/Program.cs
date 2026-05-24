using Serilog;
using Workslip.Api.Configuration;
using Workslip.Infrastructure.Schema;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    var applicationInsightsConnectionString = InfrastructureConfiguration.ResolveApplicationInsightsConnectionString(builder.Configuration);

    builder.ConfigureInfrastructure();
    builder.ConfigureLogging(applicationInsightsConnectionString);
    builder.ConfigureServices();
    builder.ConfigureAuthentication();

    var app = builder.Build();

    await using (var scope = app.Services.CreateAsyncScope())
    {
        await scope.ServiceProvider.GetRequiredService<WorkslipSchemaRunner>().ApplyAsync(CancellationToken.None);
    }

    app.ConfigurePipeline();
    app.ConfigureEndpoints();

    await app.RunAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}
