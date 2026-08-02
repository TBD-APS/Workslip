public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-ID";
    private const int MinimumAcceptedLength = 16;
    private const int MaximumAcceptedLength = 64;
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(
        RequestDelegate next,
        ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var requestedCorrelationId = context.Request.Headers.TryGetValue(HeaderName, out var value)
            ? value.ToString().Trim()
            : string.Empty;
        var correlationId = IsSafeCorrelationId(requestedCorrelationId)
            ? requestedCorrelationId
            : Guid.NewGuid().ToString("N");

        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            await _next(context);
        }
    }

    private static bool IsSafeCorrelationId(string value) =>
        value.Length is >= MinimumAcceptedLength and <= MaximumAcceptedLength
        && value.All(character => char.IsAsciiHexDigit(character) || character == '-');
}
