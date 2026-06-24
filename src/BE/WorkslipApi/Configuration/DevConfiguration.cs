using Scalar.AspNetCore;
using Workslip.Api.Endpoints;

namespace Workslip.Api.Configuration
{
    public static class DevConfiguration
    {
        public static WebApplication ConfigureDevEnvironment(this WebApplication app)
        {
            app.MapOpenApi();
            if (app.Environment.IsDevelopment())
            {
                app.MapScalarApiReference();
                app.MapDevEndpoints();
                app.UseDeveloperExceptionPage();            
            }

            return app;
        }
    }
}
