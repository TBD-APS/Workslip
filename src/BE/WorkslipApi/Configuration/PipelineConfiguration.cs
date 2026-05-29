using Serilog;
using Workslip.Api.Middleware;

namespace Workslip.Api.Configuration;

public static class PipelineConfiguration
{
    public static WebApplication ConfigurePipeline(this WebApplication app)
    {
        app.UseSecurityHeaders();

        app.UseMiddleware<CorrelationIdMiddleware>();

        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "{RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("TraceId", httpContext.TraceIdentifier);
                diagnosticContext.Set("RequestId", httpContext.Request.Headers.TryGetValue("X-Request-ID", out var requestId) ? requestId.ToString() : httpContext.TraceIdentifier);
                diagnosticContext.Set("Host", httpContext.Request.Host.Value);
                diagnosticContext.Set("Endpoint", httpContext.GetEndpoint()?.DisplayName);
                diagnosticContext.Set("QueryKeys", string.Join(",", httpContext.Request.Query.Keys));
            };
        });

        app.UseMiddleware<GlobalExceptionMiddleware>();

        app.UseRouting();
        app.UseCors(x =>
        {
            x.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
        });
        app.UseAuthentication();
        app.UseAuthorization();

        return app;
    }
}
