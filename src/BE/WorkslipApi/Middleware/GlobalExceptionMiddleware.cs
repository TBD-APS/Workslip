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
            logger.LogWarning("Request aborted by client. {Method} {Path}. TraceId: {TraceId}",
                context.Request.Method,
                context.Request.Path,
                GetTraceId(context));
        }
        catch (Exception exception)
        {
            await WriteProblemResponseAsync(context, exception);
        }
    }

    private async Task WriteProblemResponseAsync(HttpContext context, Exception exception)
    {
        var traceId = GetTraceId(context);

        logger.LogError(exception,
            "Unhandled exception while processing {Method} {Path}. TraceId: {TraceId}",
            context.Request.Method,
            context.Request.Path,
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
            Detail = "The request could not be completed. Contact support with the traceId.",
            Instance = context.Request.Path
        };
        problem.Extensions["traceId"] = traceId;

        await context.Response.WriteAsJsonAsync(problem);
    }

    private static string GetTraceId(HttpContext context) =>
        Activity.Current?.Id ?? context.TraceIdentifier;
}
