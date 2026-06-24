using Scalar.AspNetCore;
using Workslip.Api.Endpoints;

namespace Workslip.Api.Configuration
{
    public static class DevConfiguration
    {
        public static WebApplication ConfigureDevEnvironment(this WebApplication app)
        {
            //if (app.Environment.IsDevelopment())
            //{
                app.MapOpenApi();
                app.MapDevEndpoints();
                app.MapScalarApiReference();
                app.UseDeveloperExceptionPage();            
            //}

            return app;
        }
    }
}
