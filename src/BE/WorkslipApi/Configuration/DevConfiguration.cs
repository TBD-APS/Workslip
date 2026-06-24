using Scalar.AspNetCore;
using Workslip.Api.Endpoints;

namespace Workslip.Api.Configuration
{
    public static class DevConfiguration
    {
        public static WebApplication ConfigureDevEnvironment(this WebApplication app)
        {
            app.MapOpenApi();
            app.MapScalarApiReference();

            if (app.Environment.IsDevelopment())
            {
                app.MapDevEndpoints();
                app.UseDeveloperExceptionPage();            
            }

            return app;
        }
    }
}
