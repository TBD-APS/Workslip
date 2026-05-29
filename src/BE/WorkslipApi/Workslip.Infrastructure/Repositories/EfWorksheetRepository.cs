using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Application.Worksheets;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Resilience;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfWorksheetRepository : IWorksheetRepository
{
    private readonly SqlDbContext _dbContext;
    private readonly ICurrentUserContext _currentUser;
    private readonly IDatabaseRetryPolicy _retryPolicy;

    public EfWorksheetRepository(SqlDbContext dbContext, ICurrentUserContext currentUser, IDatabaseRetryPolicy retryPolicy)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _retryPolicy = retryPolicy;
    }

    public Task<WorksheetResponse> UpsertAsync(CreateWorksheetRequest request, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("worksheets.upsert", token => UpsertAsyncCoreAsync(request, token), cancellationToken);

    private async Task<WorksheetResponse> UpsertAsyncCoreAsync(CreateWorksheetRequest request, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var workDate = request.WorkDate.ToDateTime(TimeOnly.MinValue);

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == request.UserId && u.OrganizationId == _currentUser.OrganizationId, cancellationToken);
        if (user is null)
            throw new InvalidOperationException($"User with ID {request.UserId} not found");

        var job = await _dbContext.JobReports
            .FirstOrDefaultAsync(j => j.Id == request.JobId && j.OrganizationId == _currentUser.OrganizationId, cancellationToken);
        if (job is null)
            throw new InvalidOperationException($"Job with ID {request.JobId} not found");

        var stale = _dbContext.Worksheets.Local
            .FirstOrDefault(w => w.JobId == request.JobId && w.UserId == request.UserId && w.WorkDate == workDate);
        if (stale is not null)
            _dbContext.Entry(stale).State = EntityState.Detached;

        var existing = await _dbContext.Worksheets
            .FirstOrDefaultAsync(w => w.JobId == request.JobId && w.UserId == request.UserId && w.WorkDate == workDate, cancellationToken);

        if (existing is not null)
        {
            existing.HoursWorked = request.HoursWorked;
            existing.SleptOnJob = request.SleptOnJob;
            existing.UpdatedAt = now;
        }
        else
        {
            existing = new WorksheetRow
            {
                Id = Guid.NewGuid(),
                OrganizationId = user.OrganizationId,
                JobId = request.JobId,
                UserId = request.UserId,
                WorkDate = workDate,
                HoursWorked = request.HoursWorked,
                SleptOnJob = request.SleptOnJob,
                CreatedAt = now,
                UpdatedAt = now
            };
            _dbContext.Worksheets.Add(existing);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new WorksheetResponse(
            existing.Id,
            existing.OrganizationId,
            existing.JobId,
            existing.UserId,
            request.WorkDate,
            existing.HoursWorked,
            existing.SleptOnJob,
            existing.CreatedAt,
            existing.UpdatedAt);
    }

    public Task DeleteAsync(Guid worksheetId, Guid jobId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("worksheets.delete", token => DeleteAsyncCoreAsync(worksheetId, jobId, token), cancellationToken);

    private async Task DeleteAsyncCoreAsync(Guid worksheetId, Guid jobId, CancellationToken cancellationToken)
    {
        var stale = _dbContext.Worksheets.Local
            .FirstOrDefault(w => w.Id == worksheetId && w.JobId == jobId);
        if (stale is not null)
            _dbContext.Entry(stale).State = EntityState.Detached;

        var existing = await _dbContext.Worksheets
            .FirstOrDefaultAsync(
                w => w.Id == worksheetId && w.JobId == jobId && w.OrganizationId == _currentUser.OrganizationId, cancellationToken);

        if (existing is null)
            return;

        _dbContext.Worksheets.Remove(existing);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<IReadOnlyList<WorksheetResponse>> ListByJobAsync(Guid jobId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("worksheets.list-by-job", token => ListByJobAsyncCoreAsync(jobId, token), cancellationToken);

    private async Task<IReadOnlyList<WorksheetResponse>> ListByJobAsyncCoreAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.Worksheets
            .AsNoTracking()
            .Where(w => w.JobId == jobId && w.OrganizationId == _currentUser.OrganizationId)
            .OrderByDescending(w => w.WorkDate)
            .ThenByDescending(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

        return rows.Select(w => new WorksheetResponse(
            w.Id,
            w.OrganizationId,
            w.JobId,
            w.UserId,
            DateOnly.FromDateTime(w.WorkDate),
            w.HoursWorked,
            w.SleptOnJob,
            w.CreatedAt,
            w.UpdatedAt)).ToArray();
    }
}
