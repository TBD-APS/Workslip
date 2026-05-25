using Workslip.Application;

namespace Workslip.Api;

public sealed class CorrelationIdAccessor : ICorrelationIdAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationIdAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string CorrelationId =>
        _httpContextAccessor.HttpContext?.Items["CorrelationId"]?.ToString()
        ?? _httpContextAccessor.HttpContext?.TraceIdentifier
        ?? Guid.NewGuid().ToString("N");
}