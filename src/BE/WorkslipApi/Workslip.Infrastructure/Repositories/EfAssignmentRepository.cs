using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Resilience;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfAssignmentRepository : IAssignmentRepository
{
    private readonly SqlDbContext _dbContext;
    private readonly IDatabaseRetryPolicy _retryPolicy;
    private readonly ICurrentUserContext _currentUser;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public EfAssignmentRepository(SqlDbContext dbContext, IDatabaseRetryPolicy retryPolicy, ICurrentUserContext currentUser)
    {
        _dbContext = dbContext;
        _retryPolicy = retryPolicy;
        _currentUser = currentUser;
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

        _dbContext.ChangeTracker.Clear();

        var reports = await (
            from r in _dbContext.JobReports.AsNoTracking()
            join a in _dbContext.JobAssignments.AsNoTracking() on new { r.Id, r.OrganizationId } equals new { Id = a.ReportId, a.OrganizationId }
            where r.OrganizationId == organizationId && a.UserId == userId && !r.IsSoftDeleted && r.Status != "Completed"
            orderby r.UpdatedAt descending
            select r).AsNoTracking().ToListAsync(cancellationToken);

        return reports.Select(row => new JobListItemResponse(
            row.Id, row.OrganizationId, null,
            row.ReportNumber, Enum.Parse<JobStatus>(row.Status, ignoreCase: true), ToDateOnly(row.ReportDate),
            FromJsonList(row.InstallationTypesJson), row.WorkKind, row.CustomWorkKind,
            row.CreatedAt, row.UpdatedAt, row.SubmittedAt,
            [], row.IsSoftDeleted, row.DeletionScheduledAt, null)).ToArray();
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<AssignedUserResponse>>> GetAssignedUsersByReportAsync(
        Guid organizationId, IEnumerable<Guid> reportIds, CancellationToken cancellationToken)
    {
        var normalizedIds = reportIds.Distinct().ToArray();
        if (normalizedIds.Length == 0) return new Dictionary<Guid, IReadOnlyList<AssignedUserResponse>>();

        var rows = await (
            from a in _dbContext.JobAssignments.AsNoTracking()
            join u in _dbContext.Users.AsNoTracking() on new { OrganizationId = a.OrganizationId, Id = a.UserId } equals new { u.OrganizationId, u.Id }
            where a.OrganizationId == organizationId && normalizedIds.Contains(a.ReportId)
            orderby a.AssignedAt, u.DisplayName
            select new { a.ReportId, u.Id, u.DisplayName }
        ).ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.ReportId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(r => new AssignedUserResponse(r.Id, r.DisplayName)).ToArray() as IReadOnlyList<AssignedUserResponse>);
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
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == organizationId, cancellationToken);

        if (row is null) return null;

        CustomerRow? customer = null;
        if (row.CustomerId.HasValue)
        {
            customer = await _dbContext.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == row.CustomerId.Value && c.OrganizationId == organizationId, cancellationToken);
        }

        var assignedUsers = await GetSingleAssignedUsersAsync(organizationId, id, cancellationToken);

        return ToResponse(row, customer, assignedUsers);
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
        IReadOnlyList<AssignedUserResponse> assignedUsers)
    {
        return new(
            row.Id, row.OrganizationId,
            customer is not null ? new CustomerInfo(customer.Id, customer.Name, customer.Address, customer.Email, customer.ContactPerson, customer.Phone) : null,
            row.ReportNumber, Enum.Parse<JobStatus>(row.Status, ignoreCase: true), ToDateOnly(row.ReportDate),
            row.TaskDescription, row.CustomerObservations, row.TechnicalObservations,
            FromJsonList(row.InstallationTypesJson), row.WorkKind, row.CustomWorkKind,
            row.Remarks, FromJsonList(row.ClosureFlagsJson),
            [], [], 
            row.CreatedAt, row.UpdatedAt, row.SubmittedAt,
            assignedUsers, [],
            row.IsSoftDeleted, row.DeletionScheduledAt, null);
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
