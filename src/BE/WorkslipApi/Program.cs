using System.Diagnostics;
using Serilog;
using Workslip.Api.Configuration;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

var startupStopwatch = Stopwatch.StartNew();
var applicationStarted = false;
Log.Information("[STARTUP] Workslip.Api bootstrap started");

try
{
    var builder = RunStartupValuePhase(
        1,
        "Create application builder",
        () => WebApplication.CreateBuilder(args));

    Log.Information(
        "[STARTUP] Environment: {EnvironmentName}",
        builder.Environment.EnvironmentName);

    RunStartupPhase(2, "Load infrastructure configuration", () =>
    {
        builder.ConfigureInfrastructure(args);
    });

    var applicationInsightsConnectionString = builder.Configuration["Azure:ApplicationInsights:ConnectionString"];

    RunStartupPhase(3, "Configure CORS", () =>
    {
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
    });

    RunStartupPhase(4, "Configure authentication", () =>
    {
        builder.ConfigureAuthentication();
    });

    RunStartupPhase(5, "Configure logging and telemetry", () =>
    {
        builder.ConfigureLogging(applicationInsightsConnectionString);
    });

    RunStartupPhase(6, "Register application services", () =>
    {
        builder.ConfigureServices();
    });

    var app = RunStartupValuePhase(7, "Build application host", builder.Build);
    var releaseTestingEnabled = ReleaseTestingConfiguration.IsEnabled(
        app.Environment,
        app.Configuration);
    var seedDevelopmentData = DatabaseStartup.ShouldSeedDevelopmentData(app.Environment);

    await RunStartupPhaseAsync(8, "Verify database readiness", () =>
        DatabaseStartup.VerifyIfRequiredAsync(
            app.Services,
            app.Configuration,
            seedDevelopmentData));

    RunStartupPhase(9, "Configure HTTP pipeline", () =>
    {
        app.ConfigurePipeline();
    });

    RunStartupPhase(10, "Map endpoints and environment-specific features", () =>
    {
        app.ConfigureEndpoints();
        app.ConfigureDevEnvironment(releaseTestingEnabled);
    });

    Log.Information("[STARTUP 11] Start application host - START");
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        applicationStarted = true;
        Log.Information(
            "[STARTUP] READY - Workslip.Api started successfully in {ElapsedMilliseconds} ms",
            startupStopwatch.ElapsedMilliseconds);
    });

    await app.RunAsync();
}
catch (Exception exception)
{
    if (applicationStarted)
    {
        Log.Fatal(exception, "[HOST] Workslip.Api terminated because of an unhandled runtime exception");
    }
    else
    {
        Log.Fatal(exception, "[STARTUP] Workslip.Api terminated because of an unhandled startup exception");
    }

    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static void RunStartupPhase(int step, string phase, Action action)
{
    var stopwatch = Stopwatch.StartNew();
    Log.Information("[STARTUP {StartupStep:00}] {StartupPhase} - START", step, phase);

    try
    {
        action();
        Log.Information(
            "[STARTUP {StartupStep:00}] {StartupPhase} - OK ({ElapsedMilliseconds} ms)",
            step,
            phase,
            stopwatch.ElapsedMilliseconds);
    }
    catch (Exception exception)
    {
        Log.Error(
            "[STARTUP {StartupStep:00}] {StartupPhase} - FAILED after {ElapsedMilliseconds} ms ({ExceptionType})",
            step,
            phase,
            stopwatch.ElapsedMilliseconds,
            exception.GetType().Name);
        throw;
    }
}

static T RunStartupValuePhase<T>(int step, string phase, Func<T> action)
{
    var stopwatch = Stopwatch.StartNew();
    Log.Information("[STARTUP {StartupStep:00}] {StartupPhase} - START", step, phase);

    try
    {
        var result = action();
        Log.Information(
            "[STARTUP {StartupStep:00}] {StartupPhase} - OK ({ElapsedMilliseconds} ms)",
            step,
            phase,
            stopwatch.ElapsedMilliseconds);
        return result;
    }
    catch (Exception exception)
    {
        Log.Error(
            "[STARTUP {StartupStep:00}] {StartupPhase} - FAILED after {ElapsedMilliseconds} ms ({ExceptionType})",
            step,
            phase,
            stopwatch.ElapsedMilliseconds,
            exception.GetType().Name);
        throw;
    }
}

static async Task RunStartupPhaseAsync(int step, string phase, Func<Task> action)
{
    var stopwatch = Stopwatch.StartNew();
    Log.Information("[STARTUP {StartupStep:00}] {StartupPhase} - START", step, phase);

    try
    {
        await action();
        Log.Information(
            "[STARTUP {StartupStep:00}] {StartupPhase} - OK ({ElapsedMilliseconds} ms)",
            step,
            phase,
            stopwatch.ElapsedMilliseconds);
    }
    catch (Exception exception)
    {
        Log.Error(
            "[STARTUP {StartupStep:00}] {StartupPhase} - FAILED after {ElapsedMilliseconds} ms ({ExceptionType})",
            step,
            phase,
            stopwatch.ElapsedMilliseconds,
            exception.GetType().Name);
        throw;
    }
}
