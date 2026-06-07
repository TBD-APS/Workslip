using Scalar.AspNetCore;
using Workslip.Api.Endpoints;
using Workslip.Infrastructure;
using Workslip.Infrastructure.Schema;

namespace Workslip.Api.Configuration
{
    public static class DevConfiguration
    {
        public static WebApplication ConfigureDevEnvironment(this WebApplication app)
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
            app.MapDevEndpoints();
            app.UseDeveloperExceptionPage();

            return app;
        }
    }
}
