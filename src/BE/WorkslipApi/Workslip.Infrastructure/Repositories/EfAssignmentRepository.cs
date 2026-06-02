using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Application.Worksheets;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Mappers;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfAssignmentRepository : IAssignmentRepository
{
    private readonly SqlDbContext _dbContext;
    private readonly IDatabaseRetryPolicy _retryPolicy;
    private readonly ICurrentUserContext _currentUser;
    private readonly IWorksheetRepository _worksheetRepo;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public EfAssignmentRepository(SqlDbContext dbContext, IDatabaseRetryPolicy retryPolicy,
        ICurrentUserContext currentUser, IWorksheetRepository worksheetRepo)
    {
        _dbContext = dbContext;
        _retryPolicy = retryPolicy;
        _currentUser = currentUser;
        _worksheetRepo = worksheetRepo;
    }

    public Task AssignAsync(Guid jobId, Guid organizationId, IReadOnlyList<Guid> userIds, Guid? actorId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.assign", token => AssignAsyncCoreAsync(jobId, organizationId, userIds, actorId, token), cancellationToken);

    private async Task AssignAsyncCoreAsync(Guid jobId, Guid organizationId, IReadOnlyList<Guid> userIds, Guid? actorId, CancellationToken cancellationToken)
    {
        if (organizationId != _currentUser.OrganizationId)
            return;

        _dbContext.ChangeTracker.Clear();

        var existing = await _dbContext.JobReports.FirstOrDefaultAsync(r => r.Id == jobId && r.OrganizationId == organizationId, cancellationToken);

        if (existing is null) 
            return;


        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existingAssignedUsers = await GetSingleAssignedUsersAsync(organizationId, jobId, cancellationToken);
        var targetAssignedUsers = await GetAssignedUsersByIdsAsync(organizationId, userIds, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        
        await _dbContext.JobAssignments
            .Where(a => a.ReportId == jobId && a.OrganizationId == organizationId)
            .ExecuteDeleteAsync(cancellationToken);

        if (userIds.Count >= 1)
        {
            await AddAssignedUsersAsync(organizationId, jobId, userIds, actorId, now, cancellationToken);

            var entry = _dbContext.Entry(existing);
            entry.Property(e => e.UpdatedAt).CurrentValue = now;
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        var eventType = existingAssignedUsers.Count == 0 ? "assigned" : "reassigned";
        var before = JobReportMapper.ToJsonNode(new { assignedUsers = existingAssignedUsers });
        var after = JobReportMapper.ToJsonNode(new { assignedUsers = targetAssignedUsers });

        await InsertEventAsync(organizationId, jobId, actorId, eventType, before, after, now, cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public Task<IReadOnlyList<JobListItemResponse>> GetMyAssignedJobsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.get-my-assigned", token => GetMyAssignedJobsAsyncCoreAsync(organizationId, userId, token), cancellationToken);

    private async Task<IReadOnlyList<JobListItemResponse>> GetMyAssignedJobsAsyncCoreAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        if (organizationId != _currentUser.OrganizationId)
            return [];

        var projected = await (
            from r in _dbContext.JobReports.AsNoTracking()
            join a in _dbContext.JobAssignments.AsNoTracking()
                on new { r.Id, r.OrganizationId }
                equals new { Id = a.ReportId, a.OrganizationId }
            join c in _dbContext.Customers.AsNoTracking() on new { Id = (Guid?)r.CustomerId, OrganizationId = r.OrganizationId } equals new { Id = (Guid?)c.Id, c.OrganizationId } into rjc
            from c in rjc.DefaultIfEmpty()
            where r.OrganizationId == organizationId
                  && a.UserId == userId
                  && !r.IsSoftDeleted
            orderby r.UpdatedAt descending
            select new
            {
                r.Id,
                r.OrganizationId,
                CustId = r.CustomerId,
                CustName = c != null ? c.Name : null,
                CustAddress = c != null ? c.Address : null,
                CustEmail = c != null ? c.Email : null,
                CustContactPerson = c != null ? c.ContactPerson : null,
                CustPhone = c != null ? c.Phone : null,
                r.ReportNumber,
                r.Status,
                r.ReportDate,
                WorkKind = r.WorkKindRow != null ? new JobWorkKindResponse(
                    r.WorkKindRow.Id,
                    r.WorkKindRow.NormalizedLabel,
                    r.WorkKindRow.Label,
                    r.WorkKindRow.RequiresCustomWorkKind,
                    r.WorkKindRow.SortOrder,
                    r.CustomWorkKind) : null,
                r.CreatedAt,
                r.UpdatedAt,
                r.SubmittedAt,
                r.IsSoftDeleted,
                r.DeletionScheduledAt
            }).ToListAsync(cancellationToken);

        var reportIds = projected.Select(x => x.Id).ToArray();

        var assignedUsers = await (
            from a in _dbContext.JobAssignments.AsNoTracking()
            join u in _dbContext.Users.AsNoTracking()
                on new { a.UserId, a.OrganizationId }
                equals new { UserId = u.Id, u.OrganizationId }
            where a.OrganizationId == organizationId
                  && reportIds.Contains(a.ReportId)
            select new
            {
                a.ReportId,
                u.Id,
                u.DisplayName
            }).ToListAsync(cancellationToken);

        var assignedDictionary = assignedUsers
            .GroupBy(x => x.ReportId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => new AssignedUserResponse(x.Id, x.DisplayName)).ToArray() as IReadOnlyList<AssignedUserResponse>);

        var installationTypesByReport = await _dbContext.JobReportInstallations
            .AsNoTracking()
            .Where(it => it.OrganizationId == organizationId && reportIds.Contains(it.JobReportId))
            .Include(it => it.InstallationTypeDefinition)
            .GroupBy(it => it.JobReportId)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.OrderBy(it => it.SortOrder).Select(it => it.InstallationTypeDefinition.Name).ToArray() as IReadOnlyList<string>,
                cancellationToken);

        var totalHoursByJob = await _worksheetRepo.GetTotalHoursByJobAsync(reportIds, cancellationToken);

        return projected.Select(x =>
        {
            var customerInfo = x.CustId is not null
                ? new CustomerInfo(x.CustId.Value, x.CustName ?? "", x.CustAddress, x.CustEmail, x.CustContactPerson, x.CustPhone)
                : null;

            return new JobListItemResponse(
                x.Id, x.OrganizationId,
                customerInfo,
                x.ReportNumber, Enum.Parse<JobStatus>(x.Status, ignoreCase: true), JobReportMapper.ToDateOnly(x.ReportDate),
                installationTypesByReport.GetValueOrDefault(x.Id) ?? [], x.WorkKind,
                x.CreatedAt, x.UpdatedAt, x.SubmittedAt,
                assignedDictionary.GetValueOrDefault(x.Id) ?? [],
                x.IsSoftDeleted, x.DeletionScheduledAt,
                totalHoursByJob.GetValueOrDefault(x.Id));
        }).ToArray();
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<AssignedUserResponse>>> GetAssignedUsersByReportAsync(
        Guid organizationId, IEnumerable<Guid> reportIds, CancellationToken cancellationToken)
    {
        var normalizedIds = reportIds.Distinct().ToArray();
        if (normalizedIds.Length == 0) return new Dictionary<Guid, IReadOnlyList<AssignedUserResponse>>();

        var rows = await (
            from a in _dbContext.JobAssignments.AsNoTracking()
            join user in _dbContext.Users.AsNoTracking() on new { a.OrganizationId, Id = a.UserId } equals new { user.OrganizationId, user.Id }
            where a.OrganizationId == organizationId && normalizedIds.Contains(a.ReportId)
            select new { a.ReportId, user.Id, user.DisplayName }
        ).ToListAsync(cancellationToken);

        var result = rows
            .GroupBy(r => r.ReportId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new AssignedUserResponse(r.Id, r.DisplayName)).ToArray() as IReadOnlyList<AssignedUserResponse>);

        return result;
    }

    public async Task<IReadOnlyList<AssignedUserResponse>> GetAssignedUsersByIdsAsync(
        Guid organizationId, IReadOnlyList<Guid> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0) return [];

        var rows = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.OrganizationId == organizationId && userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        return userIds
            .Where(rows.ContainsKey)
            .Select(userId => new AssignedUserResponse(userId, rows[userId].DisplayName))
            .ToArray();
    }

    public async Task AddAssignedUsersAsync(
        Guid organizationId, Guid reportId,
        IReadOnlyList<Guid> userIds, Guid? actorId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var userId in userIds)
        {
            _dbContext.JobAssignments.Add(new JobAssignmentRow
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                ReportId = reportId,
                UserId = userId,
                AssignedByUserId = actorId,
                AssignedAt = now
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<AssignedUserResponse>> GetSingleAssignedUsersAsync(
        Guid organizationId, Guid reportId, CancellationToken cancellationToken) =>
        (await GetAssignedUsersByReportAsync(organizationId, [reportId], cancellationToken)).GetValueOrDefault(reportId) ?? [];

    private async Task InsertEventAsync(
        Guid organizationId, Guid reportId, Guid? actorId,
        string eventType, JsonObject? before, JsonObject? after,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        _dbContext.JobEvents.Add(new JobEventRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ReportId = reportId,
            ActorId = actorId,
            EventType = eventType,
            BeforeJson = before?.ToJsonString(JsonOptions),
            AfterJson = after?.ToJsonString(JsonOptions),
            CreatedAt = now
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
