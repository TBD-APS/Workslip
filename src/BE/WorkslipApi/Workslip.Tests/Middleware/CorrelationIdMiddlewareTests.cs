using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Workslip.Tests.Middleware;

public sealed class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_PreservesSafeHexCorrelationId()
    {
        const string correlationId = "f93a41e5-5457-463b-b7f2-e37ccca69673";
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-ID"] = correlationId;
        var middleware = CreateMiddleware();

        await middleware.InvokeAsync(context);

        Assert.Equal(correlationId, context.Items["CorrelationId"]);
        Assert.Equal(correlationId, context.Response.Headers["X-Correlation-ID"].ToString());
    }

    [Fact]
    public async Task InvokeAsync_ReplacesArbitraryOrTokenLikeCorrelationId()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-ID"] = "Bearer.secret-token@example.com";
        var middleware = CreateMiddleware();

        await middleware.InvokeAsync(context);

        var actual = Assert.IsType<string>(context.Items["CorrelationId"]);
        Assert.NotEqual("Bearer.secret-token@example.com", actual);
        Assert.Equal(32, actual.Length);
        Assert.All(actual, character => Assert.True(char.IsAsciiHexDigit(character)));
        Assert.Equal(actual, context.Response.Headers["X-Correlation-ID"].ToString());
    }

    [Fact]
    public async Task InvokeAsync_ReplacesOverlongCorrelationId()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-ID"] = new string('a', 65);
        var middleware = CreateMiddleware();

        await middleware.InvokeAsync(context);

        var actual = Assert.IsType<string>(context.Items["CorrelationId"]);
        Assert.Equal(32, actual.Length);
        Assert.NotEqual(new string('a', 65), actual);
    }

    private static CorrelationIdMiddleware CreateMiddleware() =>
        new(
            _ => Task.CompletedTask,
            NullLogger<CorrelationIdMiddleware>.Instance);
}
