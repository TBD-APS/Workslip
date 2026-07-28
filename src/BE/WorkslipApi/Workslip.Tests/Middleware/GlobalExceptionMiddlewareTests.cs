using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Api.Middleware;
using Xunit;

namespace Workslip.Tests.Middleware;

public sealed class GlobalExceptionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_returns_danish_problem_details_without_exception_message()
    {
        const string technicalMessage = "Sensitive English database exception";
        var context = new DefaultHttpContext
        {
            TraceIdentifier = "test-trace-id"
        };
        context.Response.Body = new MemoryStream();

        var middleware = new GlobalExceptionMiddleware(
            _ => throw new InvalidOperationException(technicalMessage),
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        using var response = await JsonDocument.ParseAsync(context.Response.Body);
        var root = response.RootElement;

        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("Der opstod en uventet fejl.", root.GetProperty("title").GetString());
        Assert.Contains("correlationId", root.GetProperty("detail").GetString());
        Assert.Equal("test-trace-id", root.GetProperty("traceId").GetString());
        Assert.DoesNotContain(technicalMessage, root.ToString());
    }
}
