using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Mvc;

namespace Workslip.Api.Middleware;

public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            var traceId = GetTraceId(context);
            var correlationId = context.Items["CorrelationId"]?.ToString() ?? traceId;
            logger.LogWarning("Request aborted by client. {Method} {Path}. CorrelationId: {CorrelationId} TraceId: {TraceId}",
                context.Request.Method,
                context.Request.Path,
                correlationId,
                traceId);
        }
        catch (Exception exception)
        {
            await WriteProblemResponseAsync(context, exception);
        }
    }

    private async Task WriteProblemResponseAsync(HttpContext context, Exception exception)
    {
        var traceId = GetTraceId(context);
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? traceId;

        logger.LogError(exception,
            "Unhandled exception while processing {Method} {Path}. CorrelationId: {CorrelationId} TraceId: {TraceId}",
            context.Request.Method,
            context.Request.Path,
            correlationId,
            traceId);

        if (context.Response.HasStarted)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Detail = "The request could not be completed. Contact support with the correlationId or traceId.",
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = traceId;
        problem.Extensions["correlationId"] = correlationId;

        await context.Response.WriteAsJsonAsync(problem);
    }

    private static string GetTraceId(HttpContext context) =>
        Activity.Current?.Id ?? context.TraceIdentifier;
}
