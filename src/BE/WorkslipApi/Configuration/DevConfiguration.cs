using Scalar.AspNetCore;
using Workslip.Api.Endpoints;

namespace Workslip.Api.Configuration;

public static class DevConfiguration
{
    public static WebApplication ConfigureDevEnvironment(
        this WebApplication app,
        bool releaseTestingEnabled)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            app.MapDevEndpoints();
        }

        if (!releaseTestingEnabled)
        {
            app.Logger.LogInformation(
                "Development API reference endpoints are disabled. Environment={EnvironmentName}",
                app.Environment.EnvironmentName);
            return app;
        }

        if (!app.Environment.IsDevelopment())
        {
            app.Logger.LogWarning(
                "Release-testing API reference endpoints are enabled outside Development. Disable ReleaseTesting:Enabled before customer go-live.");
        }

        app.MapOpenApi();
        app.MapScalarApiReference();

        return app;
    }
}
