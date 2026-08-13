using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Worksheets;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Mappers;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;
using static Workslip.Infrastructure.Mappers.WorksheetMapper;

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

    public Task<IReadOnlyList<MyWorksheetEntryResponse>> GetWorksheetsForUserAsync(
        Guid userId,
        Guid organizationId,
        DateOnly monthStart,
        DateOnly monthEnd,
        CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "worksheets.my",
            token => GetWorksheetsForUserCoreAsync(userId, organizationId, monthStart, monthEnd, token),
            cancellationToken);

    public Task<IReadOnlyList<MyWorksheetEntryResponse>> GetAllWorksheetsAsync(
        Guid organizationId,
        DateOnly monthStart,
        DateOnly monthEnd,
        CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "worksheets.all",
            token => GetAllWorksheetsCoreAsync(organizationId, monthStart, monthEnd, token),
            cancellationToken);

    private async Task<IReadOnlyList<MyWorksheetEntryResponse>> GetAllWorksheetsCoreAsync(
        Guid organizationId,
        DateOnly monthStart,
        DateOnly monthEnd,
        CancellationToken cancellationToken)
    {
        var fromDate = monthStart.ToDateTime(TimeOnly.MinValue);
        var toDate = monthEnd.ToDateTime(TimeOnly.MaxValue);

        var rows = await (
            from w in _dbContext.Worksheets.AsNoTracking()
            join u in _dbContext.Users.AsNoTracking() on w.UserId equals u.Id
            join r in _dbContext.JobReports.AsNoTracking() on w.JobId equals r.Id
            join c in _dbContext.Customers.AsNoTracking() on new { Id = r.CustomerId, r.OrganizationId } equals new { Id = (Guid?)c.Id, c.OrganizationId } into rjc
            from c in rjc.DefaultIfEmpty()
            where w.OrganizationId == organizationId
                && r.OrganizationId == organizationId
                && w.WorkDate >= fromDate
                && w.WorkDate <= toDate
                && !r.IsSoftDeleted
                && u.OrganizationId == organizationId
            orderby u.DisplayName
            select new WorksheetMyProjection
            {
                WorksheetId = w.Id,
                WorkDate = w.WorkDate,
                JobId = w.JobId,
                UserId = w.UserId,
                ReportNumber = r.ReportNumber,
                CustomerName = r.CustomerName ?? (c != null ? c.Name : "Ukendt kunde"),
                CustomerAddress = r.CustomerAddress ?? (c != null ? c.Address : null),
                HasOutlay = w.SleptOnJob,
                HoursWorked = w.HoursWorked,
                UserDisplayName = u.DisplayName,
                JobType = r.JobType.ToString()
            })
            .ToListAsync(cancellationToken);

        var rates = await GetEffectiveRatesAsync(
            organizationId,
            rows.Select(row => row.WorksheetId).ToArray(),
            cancellationToken);

        return rows.Select(row =>
        {
            var rate = rates.GetValueOrDefault(row.WorksheetId);
            var amount = rate.HasValue
                ? decimal.Round(row.HoursWorked * rate.Value, 2, MidpointRounding.AwayFromZero)
                : null;

            return new MyWorksheetEntryResponse(
                DateOnly.FromDateTime(row.WorkDate),
                row.JobId,
                row.UserId,
                row.ReportNumber,
                row.CustomerName,
                row.CustomerAddress,
                row.HoursWorked,
                row.HasOutlay,
                row.UserDisplayName,
                row.JobType,
                rate,
                amount);
        }).ToArray();
    }

    private async Task<IReadOnlyDictionary<Guid, decimal?>> GetEffectiveRatesAsync(
        Guid organizationId,
        IReadOnlyCollection<Guid> worksheetIds,
        CancellationToken cancellationToken)
    {
        if (worksheetIds.Count == 0)
            return new Dictionary<Guid, decimal?>();

        var connection = _dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
            await connection.OpenAsync(cancellationToken);

        try
        {
            var transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
            var worksheets = WorksheetBillingSnapshots.TableName(_dbContext, "Worksheets");
            var jobs = WorksheetBillingSnapshots.TableName(_dbContext, "JobReports");
            var rates = WorksheetBillingSnapshots.TableName(_dbContext, "UserBillingRates");
            var snapshots = WorksheetBillingSnapshots.TableName(_dbContext, "WorksheetBillingSnapshots");

            var command = new CommandDefinition(
                $"""
                 SELECT
                     worksheet.Id AS WorksheetId,
                     CASE
                         WHEN job.Status = @ApprovedStatus THEN snapshot.BillableHourlyRateSnapshot
                         ELSE rate.BillableHourlyRate
                     END AS BillableHourlyRate
                 FROM {worksheets} AS worksheet
                 INNER JOIN {jobs} AS job
                     ON job.Id = worksheet.JobId
                     AND job.OrganizationId = worksheet.OrganizationId
                 LEFT JOIN {rates} AS rate
                     ON rate.OrganizationId = worksheet.OrganizationId
                     AND rate.UserId = worksheet.UserId
                 LEFT JOIN {snapshots} AS snapshot
                     ON snapshot.OrganizationId = worksheet.OrganizationId
                     AND snapshot.WorksheetId = worksheet.Id
                 WHERE worksheet.OrganizationId = @OrganizationId
                   AND worksheet.Id IN @WorksheetIds;
                 """,
                new
                {
                    ApprovedStatus = JobStatus.Approved.ToString(),
                    OrganizationId = organizationId,
                    WorksheetIds = worksheetIds.ToArray()
                },
                transaction,
                cancellationToken: cancellationToken);

            var rows = await connection.QueryAsync<WorksheetRateProjection>(command);
            return rows.ToDictionary(row => row.WorksheetId, row => row.BillableHourlyRate);
        }
        finally
        {
            if (shouldClose)
                await connection.CloseAsync();
        }
    }

    private async Task<IReadOnlyList<MyWorksheetEntryResponse>> GetWorksheetsForUserCoreAsync(
        Guid userId,
        Guid organizationId,
        DateOnly monthStart,
        DateOnly monthEnd,
        CancellationToken cancellationToken)
    {
        var fromDate = monthStart.ToDateTime(TimeOnly.MinValue);
        var toDate = monthEnd.ToDateTime(TimeOnly.MaxValue);

        var rows = await (
            from w in _dbContext.Worksheets.AsNoTracking()
            join r in _dbContext.JobReports.AsNoTracking() on w.JobId equals r.Id
            join c in _dbContext.Customers.AsNoTracking() on new { Id = r.CustomerId, r.OrganizationId } equals new { Id = (Guid?)c.Id, c.OrganizationId } into rjc
            from c in rjc.DefaultIfEmpty()
            where w.UserId == userId
                && w.OrganizationId == organizationId
                && r.OrganizationId == organizationId
                && w.WorkDate >= fromDate
                && w.WorkDate <= toDate
                && !r.IsSoftDeleted
            orderby w.WorkDate, r.ReportNumber, (r.CustomerName ?? (c != null ? c.Name : null))
            select new WorksheetMapper.WorksheetMyProjection
            {
                WorksheetId = w.Id,
                WorkDate = w.WorkDate,
                JobId = w.JobId,
                UserId = w.UserId,
                ReportNumber = r.ReportNumber,
                CustomerName = r.CustomerName ?? (c != null ? c.Name : "Ukendt kunde"),
                CustomerAddress = r.CustomerAddress ?? (c != null ? c.Address : null),
                HasOutlay = w.SleptOnJob,
                HoursWorked = w.HoursWorked,
                JobType = r.JobType.ToString()
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new MyWorksheetEntryResponse(
            DateOnly.FromDateTime(row.WorkDate),
            row.JobId,
            row.UserId,
            row.ReportNumber,
            row.CustomerName,
            row.CustomerAddress,
            row.HoursWorked,
            row.HasOutlay,
            UserDisplayName: row.UserDisplayName,
            row.JobType)).ToArray();
    }

    public Task<WorksheetResponse> UpsertAsync(UpsertWorksheetRequest request, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("worksheets.upsert", token => UpsertAsyncCoreAsync(request, token), cancellationToken);

    private async Task<WorksheetResponse> UpsertAsyncCoreAsync(UpsertWorksheetRequest request, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var workDate = request.WorkDate.ToDateTime(TimeOnly.MinValue);

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == request.UserId && u.OrganizationId == _currentUser.OrganizationId, cancellationToken)
            ?? throw new InvalidOperationException($"User with ID {request.UserId} not found");

        var job = await _dbContext.JobReports
            .FirstOrDefaultAsync(j => j.Id == request.JobId && j.OrganizationId == _currentUser.OrganizationId, cancellationToken)
            ?? throw new InvalidOperationException($"Job with ID {request.JobId} not found");

        var stale = _dbContext.Worksheets.Local
            .FirstOrDefault(w => request.Id.HasValue
                ? w.Id == request.Id.Value && w.JobId == request.JobId
                : w.JobId == request.JobId && w.UserId == request.UserId && w.WorkDate == workDate);
        if (stale is not null)
            _dbContext.Entry(stale).State = EntityState.Detached;

        var existing = request.Id.HasValue
            ? await _dbContext.Worksheets
                .FirstOrDefaultAsync(
                    w => w.Id == request.Id.Value && w.JobId == request.JobId && w.OrganizationId == _currentUser.OrganizationId,
                    cancellationToken)
            : await _dbContext.Worksheets
                .FirstOrDefaultAsync(w => w.JobId == request.JobId && w.UserId == request.UserId && w.WorkDate == workDate, cancellationToken);

        if (request.Id.HasValue && existing is null)
            throw new InvalidOperationException("Worksheet not found");

        var existingId = existing?.Id;
        var existingHoursForUserDay = await _dbContext.Worksheets
            .AsNoTracking()
            .Where(w => w.OrganizationId == _currentUser.OrganizationId
                && w.UserId == request.UserId
                && w.WorkDate == workDate
                && (!existingId.HasValue || w.Id != existingId.Value))
            .SumAsync(w => w.HoursWorked, cancellationToken);

        if (existingHoursForUserDay + request.HoursWorked > 24m)
            throw new InvalidOperationException("Worksheet daily hours cannot exceed 24 hours for the selected user");

        if (existing is not null)
        {
            existing.UserId = request.UserId;
            existing.WorkDate = workDate;
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
        await transaction.CommitAsync(cancellationToken);

        return new WorksheetResponse(
            existing.Id,
            existing.OrganizationId,
            existing.JobId,
            existing.UserId,
            user.DisplayName,
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
        var rows = await (
            from worksheet in _dbContext.Worksheets.AsNoTracking()
            join user in _dbContext.Users.AsNoTracking() on worksheet.UserId equals user.Id
            where worksheet.JobId == jobId
                && worksheet.OrganizationId == _currentUser.OrganizationId
                && user.OrganizationId == _currentUser.OrganizationId
            orderby worksheet.WorkDate, user.DisplayName
            select new { Worksheet = worksheet, user.DisplayName }
        ).ToListAsync(cancellationToken);

        return rows.Select(row => new WorksheetResponse(
            row.Worksheet.Id,
            row.Worksheet.OrganizationId,
            row.Worksheet.JobId,
            row.Worksheet.UserId,
            row.DisplayName,
            DateOnly.FromDateTime(row.Worksheet.WorkDate),
            row.Worksheet.HoursWorked,
            row.Worksheet.SleptOnJob,
            row.Worksheet.CreatedAt,
            row.Worksheet.UpdatedAt)).ToArray();
    }

    public Task<IReadOnlyList<WorksheetUserGroupResponse>> GetGroupedByJobAsync(Guid jobId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("worksheets.grouped-by-job", token => GetGroupedByJobAsyncCoreAsync(jobId, token), cancellationToken);

    private async Task<IReadOnlyList<WorksheetUserGroupResponse>> GetGroupedByJobAsyncCoreAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var rows = await (
            from w in _dbContext.Worksheets.AsNoTracking()
            join u in _dbContext.Users.AsNoTracking() on w.UserId equals u.Id
            where w.JobId == jobId
            orderby u.DisplayName, w.WorkDate descending
            select new WorksheetMapper.WorksheetEntryProjection
            {
                WorkDate = w.WorkDate,
                HoursWorked = w.HoursWorked,
                DisplayName = u.DisplayName
            }
        ).ToListAsync(cancellationToken);

        return WorksheetMapper.ToGroupedResponse(rows);
    }

    private sealed class JobTotalHoursProjection
    {
        public Guid JobId { get; init; }
        public decimal? TotalHours { get; init; }
    }

    private sealed record WorksheetRateProjection(Guid WorksheetId, decimal? BillableHourlyRate);

    public Task<decimal> GetHoursForUserDayAsync(
        Guid organizationId,
        Guid userId,
        DateOnly workDate,
        CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "worksheets.daily-hours",
            token => _dbContext.Worksheets
                .AsNoTracking()
                .Where(w => w.OrganizationId == organizationId
                    && w.UserId == userId
                    && w.WorkDate == workDate.ToDateTime(TimeOnly.MinValue))
                .SumAsync(w => w.HoursWorked, token),
            cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, decimal?>> GetTotalHoursByJobAsync(
        IEnumerable<Guid> jobIds, CancellationToken cancellationToken)
    {
        var ids = jobIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, decimal?>();

        var rows = await _dbContext.Worksheets
            .AsNoTracking()
            .Where(w => ids.Contains(w.JobId))
            .GroupBy(w => w.JobId)
            .Select(g => new JobTotalHoursProjection
            {
                JobId = g.Key,
                TotalHours = g.Sum(w => w.HoursWorked)
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.JobId, r => r.TotalHours);
    }

}
