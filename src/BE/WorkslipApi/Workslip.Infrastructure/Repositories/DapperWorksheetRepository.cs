using Workslip.Application.Worksheets;
using Workslip.Infrastructure.Resilience;

namespace Workslip.Infrastructure.Repositories;

public sealed class DapperWorksheetRepository : IWorksheetRepository
{
    // Placeholder dependencies - will be used when implementing logic
    private readonly ISqlConnectionFactory _connectionFactory;
    private readonly IDatabaseRetryPolicy _retryPolicy;

    public DapperWorksheetRepository(ISqlConnectionFactory connectionFactory, IDatabaseRetryPolicy retryPolicy)
    {
        _connectionFactory = connectionFactory;
        _retryPolicy = retryPolicy;
    }

    public Task<WorksheetResponse> CreateAsync(CreateWorksheetRequest request, CancellationToken cancellationToken)
    {
        // No logic - structure only
        throw new NotImplementedException();
    }

    public Task<WorksheetResponse> DeleteAsync(Guid worksheetId, Guid jobId, CancellationToken cancellationToken)
    {
        // No logic - structure only
        throw new NotImplementedException();
    }
}
