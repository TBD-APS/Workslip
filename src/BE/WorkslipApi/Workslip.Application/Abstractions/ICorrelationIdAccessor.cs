namespace Workslip.Application;

public interface ICorrelationIdAccessor
{
    string CorrelationId { get; }
}
