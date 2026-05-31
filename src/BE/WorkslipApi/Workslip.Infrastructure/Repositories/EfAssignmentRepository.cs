using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfAssignmentRepository : IAssignmentRepository
{
    private readonly SqlDbContext _dbContext;
    private readonly IDatabaseRetryPolicy _retryPolicy;
    private readonly ICurrentUserContext _currentUser;
    private readonly IJobLinkRepository _linkRepo;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public EfAssignmentRepository(SqlDbContext dbContext, IDatabaseRetryPolicy retryPolicy, ICurrentUserContext currentUser, IJobLinkRepository linkRepo)
    {
        _dbContext = dbContext;
        _retryPolicy = retryPolicy;
        _currentUser = currentUser;
        _linkRepo = linkRepo;
    }

    public Task<JobReportResponse?> AssignAsync(Guid jobId, Guid organizationId, IReadOnlyList<Guid> userIds, Guid? actorId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.assign", token => AssignAsyncCoreAsync(jobId, organizationId, userIds, actorId, token), cancellationToken);

    private async Task<JobReportResponse?> AssignAsyncCoreAsync(Guid jobId, Guid organizationId, IReadOnlyList<Guid> userIds, Guid? actorId, CancellationToken cancellationToken)
    {
        if (organizationId != _currentUser.OrganizationId)
            return null;

        _dbContext.ChangeTracker.Clear();

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existing = await _dbContext.JobReports
            .FirstOrDefaultAsync(r => r.Id == jobId && r.OrganizationId == organizationId, cancellationToken);

        if (existing is null) return null;

        var targetUserIds = userIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        var targetAssignedUsers = await GetAssignedUsersByIdsAsync(organizationId, targetUserIds, cancellationToken);
        if (targetAssignedUsers.Count != targetUserIds.Length)
            return null;

        var existingAssignedUsers = await GetSingleAssignedUsersAsync(organizationId, jobId, cancellationToken);
        var existingUserIdSet = existingAssignedUsers.Select(u => u.Id).OrderBy(id => id).ToArray();
        var orderedTargetIds = targetUserIds.OrderBy(id => id).ToArray();
        if (existingUserIdSet.SequenceEqual(orderedTargetIds))
            return await GetJobAsync(jobId, organizationId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        await _dbContext.JobAssignments
            .Where(a => a.ReportId == jobId && a.OrganizationId == organizationId)
            .ExecuteDeleteAsync(cancellationToken);

        await ReplaceAssignedUsersAsync(organizationId, jobId, targetUserIds, actorId, now, cancellationToken);

        var entry = _dbContext.Entry(existing);
        entry.Property(e => e.UpdatedAt).CurrentValue = now;
        await _dbContext.SaveChangesAsync(cancellationToken);

        var eventType = existingAssignedUsers.Count == 0 ? "assigned" : "reassigned";
        var before = ToJsonNode(new { assignedUsers = existingAssignedUsers });
        var after = ToJsonNode(new { assignedUsers = targetAssignedUsers });

        await InsertEventAsync(organizationId, jobId, actorId, eventType, before, after, now, cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return await GetJobAsync(jobId, organizationId, cancellationToken);
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
                r.WorkKind,
                r.CustomWorkKind,
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

        var installationTypesByReport = await _dbContext.InstallationTypeRow
            .AsNoTracking()
            .Where(it => it.OrganizationId == organizationId && reportIds.Contains(it.JobReportId))
            .GroupBy(it => it.JobReportId)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.OrderBy(it => it.SortOrder).Select(it => it.Name).ToArray() as IReadOnlyList<string>,
                cancellationToken);

        var totalHoursByJob = await GetTotalHoursByJobAsync(reportIds, cancellationToken);

        return projected.Select(x =>
        {
            var customerInfo = x.CustId is not null
                ? new CustomerInfo(x.CustId.Value, x.CustName ?? "", x.CustAddress, x.CustEmail, x.CustContactPerson, x.CustPhone)
                : null;

            return new JobListItemResponse(
                x.Id, x.OrganizationId,
                customerInfo,
                x.ReportNumber, Enum.Parse<JobStatus>(x.Status, ignoreCase: true), ToDateOnly(x.ReportDate),
                installationTypesByReport.GetValueOrDefault(x.Id) ?? [], x.WorkKind, x.CustomWorkKind,
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

    public async Task ReplaceAssignedUsersAsync(
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

    private async Task<JobReportResponse?> GetJobAsync(Guid id, Guid organizationId, CancellationToken cancellationToken)
    {
        var row = await _dbContext.JobReports
            .AsNoTracking()
            .Include(r => r.InstallationTypes)
                .ThenInclude(it => it.ControlPoints)
                    .ThenInclude(cp => cp.ControlCategory)
            .Include(r => r.InstallationTypes)
                .ThenInclude(it => it.ControlPoints)
                    .ThenInclude(cp => cp.ControlPoint)
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == organizationId, cancellationToken);

        if (row is null) return null;

        CustomerRow? customer = null;
        if (row.CustomerId.HasValue)
        {
            customer = await _dbContext.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == row.CustomerId.Value && c.OrganizationId == organizationId, cancellationToken);
        }

        var links = await LoadLinksAsync(organizationId, id, cancellationToken);
        var assignedUsers = await GetSingleAssignedUsersAsync(organizationId, id, cancellationToken);
        var totalHours = await GetTotalHoursByJobAsync([id], cancellationToken);
        var worksheetEntries = await GetWorksheetEntriesByJobAsync(id, cancellationToken);

        return ToResponse(row, customer, links, assignedUsers, worksheetEntries, totalHours.GetValueOrDefault(id));
    }

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

    private static JobReportResponse ToResponse(
        JobReportRow row,
        CustomerRow? customer,
        IReadOnlyList<JobLinkInfoResponse> links,
        IReadOnlyList<AssignedUserResponse> assignedUsers,
        IReadOnlyList<WorksheetUserGroupResponse> worksheetEntries,
        decimal? totalHours = null)
    {
        var installationTypes = row.InstallationTypes?
            .OrderBy(it => it.SortOrder)
            .Select(it =>
            {
                var categories = it.ControlPoints?
                    .GroupBy(cp => new { cp.ControlCategory.Id, cp.ControlCategory.Name, cp.ControlCategory.SortOrder })
                    .OrderBy(g => g.Key.SortOrder)
                    .Select(g => new InstallationTypeCategoryResponse(
                        g.Key.Id,
                        g.Key.Name,
                        g.Key.SortOrder,
                        g.OrderBy(cp => cp.SortOrder)
                            .Select(cp => new InstallationTypeControlPointResponse(
                                cp.ControlPoint.Id,
                                cp.ControlPoint.Name,
                                cp.ControlPoint.Description,
                                cp.SortOrder,
                                cp.IsRequired,
                                cp.ControlPoint.IsChecked))
                            .ToArray()))
                    .ToArray() ?? [];

                return new InstallationTypeResponse(it.Id, it.Name, it.Description, it.SortOrder, categories);
            })
            .ToArray() ?? [];

        return new(
            row.Id, row.OrganizationId,
            customer is not null ? new CustomerInfo(customer.Id, customer.Name, customer.Address, customer.Email, customer.ContactPerson, customer.Phone) : null,
            row.ReportNumber, Enum.Parse<JobStatus>(row.Status, ignoreCase: true), ToDateOnly(row.ReportDate),
            row.TaskDescription, row.CustomerObservations, row.TechnicalObservations,
            installationTypes, row.WorkKind, row.CustomWorkKind,
            row.Remarks, FromJsonList(row.ClosureFlagsJson), links,
            row.CreatedAt, row.UpdatedAt, row.SubmittedAt,
            assignedUsers, worksheetEntries,
            row.IsSoftDeleted, row.DeletionScheduledAt, totalHours);
    }


    private sealed class WorksheetEntryProjection
    {
        public DateTime WorkDate { get; init; }
        public decimal HoursWorked { get; init; }
        public string DisplayName { get; init; } = "";
    }

    private sealed class JobTotalHoursProjection
    {
        public Guid JobId { get; init; }
        public decimal? TotalHours { get; init; }
    }

    private async Task<IReadOnlyList<JobLinkInfoResponse>> LoadLinksAsync(Guid organizationId, Guid reportId, CancellationToken cancellationToken)
    {
        var links = await _linkRepo.GetLinkRowsAsync(organizationId, reportId, cancellationToken);

        var linkedIds = links
            .Select(l => l.SourceReportId == reportId ? l.TargetReportId : l.SourceReportId)
            .Distinct()
            .ToArray();

        if (linkedIds.Length == 0) return [];

        var linkedReports = await (
            from r in _dbContext.JobReports.AsNoTracking()
            join c in _dbContext.Customers.AsNoTracking() on new { Id = (Guid?)r.CustomerId, r.OrganizationId } equals new { Id = (Guid?)c.Id, c.OrganizationId } into rjc
            from c in rjc.DefaultIfEmpty()
            where r.OrganizationId == organizationId && linkedIds.Contains(r.Id)
            select new { r.Id, r.ReportNumber, r.Status, CustomerName = c != null ? c.Name : null }
        ).ToDictionaryAsync(r => r.Id, cancellationToken);

        return links.Select(link =>
        {
            var linkedId = link.SourceReportId == reportId ? link.TargetReportId : link.SourceReportId;
            var linked = linkedReports.GetValueOrDefault(linkedId);
            return new JobLinkInfoResponse(
                linkedId,
                linked?.ReportNumber ?? "",
                linked?.CustomerName ?? "",
                linked?.Status ?? "",
                link.LinkType);
        }).ToArray();
    }

    private async Task<IReadOnlyList<WorksheetUserGroupResponse>> GetWorksheetEntriesByJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var rows = await (
            from w in _dbContext.Worksheets.AsNoTracking()
            join u in _dbContext.Users.AsNoTracking() on w.UserId equals u.Id
            where w.JobId == jobId
            orderby u.DisplayName, w.WorkDate descending
            select new WorksheetEntryProjection
            {
                WorkDate = w.WorkDate,
                HoursWorked = w.HoursWorked,
                DisplayName = u.DisplayName
            }
        ).ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.DisplayName)
            .Select(g => new WorksheetUserGroupResponse(
                g.Key,
                g.Sum(r => r.HoursWorked),
                g.Select(r => new WorksheetDayEntry(DateOnly.FromDateTime(r.WorkDate), r.HoursWorked)).ToArray() as IReadOnlyList<WorksheetDayEntry>))
            .ToArray();
    }

    private async Task<IReadOnlyDictionary<Guid, decimal?>> GetTotalHoursByJobAsync(
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

    private static DateOnly? ToDateOnly(DateTime? value) =>
        value is null ? null : DateOnly.FromDateTime(value.Value);

    private static string ToJson<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static JsonObject ToJsonNode<T>(T value) =>
        JsonSerializer.SerializeToNode(value, JsonOptions)?.AsObject() ?? [];

    private static IReadOnlyList<string> FromJsonList(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<string>>(json, JsonOptions) ?? [];
}
