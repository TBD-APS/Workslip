using Microsoft.EntityFrameworkCore;
using System.Data;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Worksheets;
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

    public EfWorksheetRepository(
        SqlDbContext dbContext,
        ICurrentUserContext currentUser,
        IDatabaseRetryPolicy retryPolicy)
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
            from worksheet in _dbContext.Worksheets.AsNoTracking()
            join user in _dbContext.Users.AsNoTracking() on worksheet.UserId equals user.Id
            join report in _dbContext.JobReports.AsNoTracking() on worksheet.JobId equals report.Id
            join customer in _dbContext.Customers.AsNoTracking()
                on new { Id = report.CustomerId, report.OrganizationId }
                equals new { Id = (Guid?)customer.Id, customer.OrganizationId } into reportCustomerJoin
            from customer in reportCustomerJoin.DefaultIfEmpty()
            where worksheet.OrganizationId == organizationId
                && report.OrganizationId == organizationId
                && worksheet.WorkDate >= fromDate
                && worksheet.WorkDate <= toDate
                && !report.IsSoftDeleted
                && user.OrganizationId == organizationId
            orderby user.DisplayName
            select new WorksheetMyProjection
            {
                WorkDate = worksheet.WorkDate,
                JobId = worksheet.JobId,
                ReportNumber = report.ReportNumber,
                CustomerName = report.CustomerName ?? (customer != null ? customer.Name : "Ukendt kunde"),
                CustomerAddress = report.CustomerAddress ?? (customer != null ? customer.Address : null),
                HasOutlay = worksheet.SleptOnJob,
                HoursWorked = worksheet.HoursWorked,
                UserDisplayName = user.DisplayName,
                JobType = report.JobType.ToString()
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new MyWorksheetEntryResponse(
            DateOnly.FromDateTime(row.WorkDate),
            row.JobId,
            row.ReportNumber,
            row.CustomerName,
            row.CustomerAddress,
            row.HoursWorked,
            row.HasOutlay,
            row.UserDisplayName,
            row.JobType)).ToArray();
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
            from worksheet in _dbContext.Worksheets.AsNoTracking()
            join report in _dbContext.JobReports.AsNoTracking() on worksheet.JobId equals report.Id
            join customer in _dbContext.Customers.AsNoTracking()
                on new { Id = report.CustomerId, report.OrganizationId }
                equals new { Id = (Guid?)customer.Id, customer.OrganizationId } into reportCustomerJoin
            from customer in reportCustomerJoin.DefaultIfEmpty()
            where worksheet.UserId == userId
                && worksheet.OrganizationId == organizationId
                && report.OrganizationId == organizationId
                && worksheet.WorkDate >= fromDate
                && worksheet.WorkDate <= toDate
                && !report.IsSoftDeleted
            orderby worksheet.WorkDate, report.ReportNumber, (report.CustomerName ?? (customer != null ? customer.Name : null))
            select new WorksheetMyProjection
            {
                WorkDate = worksheet.WorkDate,
                JobId = worksheet.JobId,
                ReportNumber = report.ReportNumber,
                CustomerName = report.CustomerName ?? (customer != null ? customer.Name : "Ukendt kunde"),
                CustomerAddress = report.CustomerAddress ?? (customer != null ? customer.Address : null),
                HasOutlay = worksheet.SleptOnJob,
                HoursWorked = worksheet.HoursWorked,
                JobType = report.JobType.ToString()
            })
            .ToListAsync(cancellationToken);

        return rows.Select(row => new MyWorksheetEntryResponse(
            DateOnly.FromDateTime(row.WorkDate),
            row.JobId,
            row.ReportNumber,
            row.CustomerName,
            row.CustomerAddress,
            row.HoursWorked,
            row.HasOutlay,
            UserDisplayName: row.UserDisplayName,
            row.JobType)).ToArray();
    }

    public Task<WorksheetResponse> UpsertAsync(
        UpsertWorksheetRequest request,
        CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "worksheets.upsert",
            token => UpsertAsyncCoreAsync(request, token),
            cancellationToken);

    private async Task<WorksheetResponse> UpsertAsyncCoreAsync(
        UpsertWorksheetRequest request,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var workDate = request.WorkDate.ToDateTime(TimeOnly.MinValue);

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(
                candidate => candidate.Id == request.UserId
                    && candidate.OrganizationId == _currentUser.OrganizationId,
                cancellationToken)
            ?? throw new InvalidOperationException($"User with ID {request.UserId} not found");

        var job = await _dbContext.JobReports
            .FirstOrDefaultAsync(
                candidate => candidate.Id == request.JobId
                    && candidate.OrganizationId == _currentUser.OrganizationId,
                cancellationToken)
            ?? throw new InvalidOperationException($"Job with ID {request.JobId} not found");

        var stale = _dbContext.Worksheets.Local
            .FirstOrDefault(worksheet => request.Id.HasValue
                ? worksheet.Id == request.Id.Value && worksheet.JobId == request.JobId
                : worksheet.JobId == request.JobId
                    && worksheet.UserId == request.UserId
                    && worksheet.WorkDate == workDate);
        if (stale is not null)
        {
            _dbContext.Entry(stale).State = EntityState.Detached;
        }

        var existing = request.Id.HasValue
            ? await _dbContext.Worksheets.FirstOrDefaultAsync(
                worksheet => worksheet.Id == request.Id.Value
                    && worksheet.JobId == request.JobId
                    && worksheet.OrganizationId == _currentUser.OrganizationId,
                cancellationToken)
            : await _dbContext.Worksheets.FirstOrDefaultAsync(
                worksheet => worksheet.JobId == request.JobId
                    && worksheet.UserId == request.UserId
                    && worksheet.WorkDate == workDate,
                cancellationToken);

        if (request.Id.HasValue && existing is null)
        {
            throw new InvalidOperationException("Worksheet not found");
        }

        var existingId = existing?.Id;
        var existingHoursForUserDay = await _dbContext.Worksheets
            .AsNoTracking()
            .Where(worksheet => worksheet.OrganizationId == _currentUser.OrganizationId
                && worksheet.UserId == request.UserId
                && worksheet.WorkDate == workDate
                && (!existingId.HasValue || worksheet.Id != existingId.Value))
            .SumAsync(worksheet => worksheet.HoursWorked, cancellationToken);

        if (existingHoursForUserDay + request.HoursWorked > 24m)
        {
            throw new InvalidOperationException(
                "Worksheet daily hours cannot exceed 24 hours for the selected user");
        }

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
                OrganizationId = job.OrganizationId,
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

    public Task DeleteAsync(
        Guid worksheetId,
        Guid jobId,
        CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "worksheets.delete",
            token => DeleteAsyncCoreAsync(worksheetId, jobId, token),
            cancellationToken);

    private async Task DeleteAsyncCoreAsync(
        Guid worksheetId,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var stale = _dbContext.Worksheets.Local
            .FirstOrDefault(worksheet => worksheet.Id == worksheetId && worksheet.JobId == jobId);
        if (stale is not null)
        {
            _dbContext.Entry(stale).State = EntityState.Detached;
        }

        var existing = await _dbContext.Worksheets
            .FirstOrDefaultAsync(
                worksheet => worksheet.Id == worksheetId
                    && worksheet.JobId == jobId
                    && worksheet.OrganizationId == _currentUser.OrganizationId,
                cancellationToken);

        if (existing is null)
        {
            return;
        }

        _dbContext.Worksheets.Remove(existing);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<IReadOnlyList<WorksheetResponse>> ListByJobAsync(
        Guid jobId,
        CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "worksheets.list-by-job",
            token => ListByJobAsyncCoreAsync(jobId, token),
            cancellationToken);

    private async Task<IReadOnlyList<WorksheetResponse>> ListByJobAsyncCoreAsync(
        Guid jobId,
        CancellationToken cancellationToken)
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

    public Task<IReadOnlyList<WorksheetUserGroupResponse>> GetGroupedByJobAsync(
        Guid jobId,
        CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "worksheets.grouped-by-job",
            token => GetGroupedByJobAsyncCoreAsync(jobId, token),
            cancellationToken);

    private async Task<IReadOnlyList<WorksheetUserGroupResponse>> GetGroupedByJobAsyncCoreAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var rows = await (
            from worksheet in _dbContext.Worksheets.AsNoTracking()
            join user in _dbContext.Users.AsNoTracking() on worksheet.UserId equals user.Id
            where worksheet.JobId == jobId
            orderby user.DisplayName, worksheet.WorkDate descending
            select new WorksheetEntryProjection
            {
                WorkDate = worksheet.WorkDate,
                HoursWorked = worksheet.HoursWorked,
                DisplayName = user.DisplayName
            }
        ).ToListAsync(cancellationToken);

        return WorksheetMapper.ToGroupedResponse(rows);
    }

    private sealed class JobTotalHoursProjection
    {
        public Guid JobId { get; init; }
        public decimal? TotalHours { get; init; }
    }

    public async Task<IReadOnlyDictionary<Guid, decimal?>> GetTotalHoursByJobAsync(
        IEnumerable<Guid> jobIds,
        CancellationToken cancellationToken)
    {
        var ids = jobIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, decimal?>();
        }

        var rows = await _dbContext.Worksheets
            .AsNoTracking()
            .Where(worksheet => ids.Contains(worksheet.JobId))
            .GroupBy(worksheet => worksheet.JobId)
            .Select(group => new JobTotalHoursProjection
            {
                JobId = group.Key,
                TotalHours = group.Sum(worksheet => worksheet.HoursWorked)
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => row.JobId, row => row.TotalHours);
    }
}
