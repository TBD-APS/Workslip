using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Application.Jobs;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Resilience;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfJobRepository : IJobRepository
{
    private readonly SqlDbContext _dbContext;
    private readonly IDatabaseRetryPolicy _retryPolicy;
    private readonly ICurrentUserContext _currentUser;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DeletionRetentionPeriod = TimeSpan.FromDays(30);

    public EfJobRepository(SqlDbContext dbContext, IDatabaseRetryPolicy retryPolicy, ICurrentUserContext currentUser)
    {
        _dbContext = dbContext;
        _retryPolicy = retryPolicy;
        _currentUser = currentUser;
    }

    public Task<JobReportResponse> CreateAsync(Guid organizationId, CreateJobRequest request, IReadOnlyList<Guid> assignedUserIds, Guid? actorId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.create", token => CreateAsyncCoreAsync(organizationId, request, assignedUserIds, actorId, token), cancellationToken);

    private async Task<JobReportResponse> CreateAsyncCoreAsync(Guid organizationId, CreateJobRequest request, IReadOnlyList<Guid> assignedUserIds, Guid? actorId, CancellationToken cancellationToken)
    {
        if (organizationId != _currentUser.OrganizationId)
            throw new InvalidOperationException("Organization mismatch");

        _dbContext.ChangeTracker.Clear();

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var reportId = Guid.NewGuid();

        var customerId = request.Customer?.Email is not null
            ? (Guid?)await UpsertCustomerAsync(organizationId, request.Customer, cancellationToken)
            : null;

        _dbContext.JobReports.Add(new JobReportRow
        {
            Id = reportId,
            OrganizationId = organizationId,
            CustomerId = customerId,
            ReportNumber = request.ReportNumber,
            Status = JobStatus.Draft.ToString(),
            ReportDate = ToDateTime(request.ReportDate),
            TaskDescription = request.TaskDescription,
            CustomerObservations = request.CustomerObservations,
            TechnicalObservations = request.TechnicalObservations,
            InstallationTypesJson = ToJson(request.InstallationTypes ?? []),
            WorkKind = NormalizeOptional(request.WorkKind),
            CustomWorkKind = request.CustomWorkKind,
            Remarks = request.Remarks,
            ClosureFlagsJson = ToJson(request.ClosureFlags ?? []),
            CreatedAt = now,
            UpdatedAt = now
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        await ReplaceControlInstallationTypesAsync(organizationId, reportId, request.ControlInstallationTypes, now, cancellationToken);
        var normalizedUserIds = NormalizeUserIds(assignedUserIds);
        await ReplaceAssignedUsersAsync(organizationId, reportId, normalizedUserIds, actorId, now, cancellationToken);
        var assignedUsers = await LoadAssignedUsersByIdsAsync(organizationId, normalizedUserIds, cancellationToken);
        await InsertEventAsync(organizationId, reportId, actorId, "created", null, ToJsonNode(new { reportId, assignedUsers }), now, cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return (await GetAsync(reportId, organizationId, cancellationToken))!;
    }

    public Task<IReadOnlyList<JobListItemResponse>> ListAsync(JobQuery query, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.list", token => ListAsyncCoreAsync(query, token), cancellationToken);

    private async Task<IReadOnlyList<JobListItemResponse>> ListAsyncCoreAsync(JobQuery query, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        var reports = await (
            from r in _dbContext.JobReports.AsNoTracking()
            join c in _dbContext.Customers.AsNoTracking() on new { Id = (Guid?)r.CustomerId, OrganizationId = r.OrganizationId } equals new { Id = (Guid?)c.Id, c.OrganizationId } into rjc
            from c in rjc.DefaultIfEmpty()
            where r.OrganizationId == query.OrganizationId
            where query.Status == null || r.Status == query.Status.ToString()
            where query.ReportNumber == null || (r.ReportNumber != null && r.ReportNumber.Contains(query.ReportNumber))
            where query.CustomerName == null || (c != null && c.Name.Contains(query.CustomerName))
            where query.CustomerEmail == null || (c != null && c.Email != null && c.Email.Contains(query.CustomerEmail))
            where query.CustomerAddress == null || (c != null && c.Address != null && c.Address.Contains(query.CustomerAddress))
            orderby r.UpdatedAt descending
            select r
        ).Skip(query.Offset).Take(query.Limit).AsNoTracking().ToListAsync(cancellationToken);

        var reportIds = reports.Select(r => r.Id).ToArray();
        var assignedUsersByReport = await LoadAssignedUsersAsync(query.OrganizationId, reportIds, cancellationToken);
        var totalHoursByJob = await GetTotalHoursByJobAsync(reportIds, cancellationToken);

        var customerIds = reports.Where(r => r.CustomerId.HasValue).Select(r => r.CustomerId!.Value).Distinct().ToArray();
        var customers = customerIds.Length > 0
            ? await _dbContext.Customers.AsNoTracking().Where(c => c.OrganizationId == query.OrganizationId && customerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken)
            : [];

        return reports.Select(row =>
        {
            var c = row.CustomerId is not null ? customers.GetValueOrDefault(row.CustomerId.Value) : null;
            return new JobListItemResponse(
                row.Id, row.OrganizationId,
                c is not null ? new CustomerInfo(c.Id, c.Name, c.Address, c.Email, c.ContactPerson, c.Phone) : null,
                row.ReportNumber, ParseStatus(row.Status), ToDateOnly(row.ReportDate),
                FromJsonList(row.InstallationTypesJson), row.WorkKind, row.CustomWorkKind,
                row.CreatedAt, row.UpdatedAt, row.SubmittedAt,
                assignedUsersByReport.GetValueOrDefault(row.Id) ?? [],
                row.IsSoftDeleted, row.DeletionScheduledAt,
                totalHoursByJob.GetValueOrDefault(row.Id));
        }).ToArray();
    }

    public Task<IReadOnlyList<JobListItemResponse>> GetMyAssignedJobsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.get-my-assigned", token => GetMyAssignedJobsAsyncCore(organizationId, userId, token), cancellationToken);

    private async Task<IReadOnlyList<JobListItemResponse>> GetMyAssignedJobsAsyncCore(Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        if (organizationId != _currentUser.OrganizationId)
            return [];

        _dbContext.ChangeTracker.Clear();

        var reports = await (
            from r in _dbContext.JobReports.AsNoTracking()
            join a in _dbContext.JobAssignments.AsNoTracking() on new { r.Id, OrganizationId = r.OrganizationId } equals new { Id = a.ReportId, a.OrganizationId }
            where r.OrganizationId == organizationId && a.UserId == userId && !r.IsSoftDeleted && r.Status != "Completed"
            orderby r.UpdatedAt descending
            select r).AsNoTracking().ToListAsync(cancellationToken);

        return reports.Select(row => new JobListItemResponse(
            row.Id, row.OrganizationId, null,
            row.ReportNumber, ParseStatus(row.Status), ToDateOnly(row.ReportDate),
            FromJsonList(row.InstallationTypesJson), row.WorkKind, row.CustomWorkKind,
            row.CreatedAt, row.UpdatedAt, row.SubmittedAt,
            [], row.IsSoftDeleted, row.DeletionScheduledAt, null)).ToArray();
    }

    public Task<JobReportResponse?> GetAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.get", token => GetAsyncCoreAsync(id, organizationId, token), cancellationToken);

    private async Task<JobReportResponse?> GetAsyncCoreAsync(Guid id, Guid organizationId, CancellationToken cancellationToken)
    {
        if (organizationId != _currentUser.OrganizationId)
            return null;

        _dbContext.ChangeTracker.Clear();

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

        var subcategories = await _dbContext.JobControlSubcategoryDecisions
            .AsNoTracking()
            .Where(s => s.ReportId == id && s.OrganizationId == organizationId)
            .OrderBy(s => s.InstallationTypeId).ThenBy(s => s.SubcategoryId)
            .ToListAsync(cancellationToken);

        var checks = await _dbContext.JobControlChecks
            .AsNoTracking()
            .Where(c => c.ReportId == id && c.OrganizationId == organizationId)
            .OrderBy(c => c.InstallationTypeId).ThenBy(c => c.SubcategoryId).ThenBy(c => c.ItemId)
            .ToListAsync(cancellationToken);

        var links = await LoadLinksAsync(organizationId, id, cancellationToken);
        var assignedUsers = await LoadAssignedUsersAsync(organizationId, id, cancellationToken);
        var totalHours = await GetTotalHoursByJobAsync([id], cancellationToken);
        var worksheetEntries = await GetWorksheetEntriesByJobAsync(id, cancellationToken);

        return ToResponse(row, customer, subcategories, checks, links, assignedUsers, worksheetEntries, totalHours.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<JobEventResponse>?> GetEventsAsync(Guid id, Guid organizationId, int limit, int offset, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.events", token => GetEventsAsyncCoreAsync(id, organizationId, limit, offset, token), cancellationToken);

    private async Task<IReadOnlyList<JobEventResponse>?> GetEventsAsyncCoreAsync(Guid id, Guid organizationId, int limit, int offset, CancellationToken cancellationToken)
    {
        if (organizationId != _currentUser.OrganizationId)
            return null;

        _dbContext.ChangeTracker.Clear();

        var exists = await _dbContext.JobReports.AsNoTracking().AnyAsync(r => r.Id == id && r.OrganizationId == organizationId, cancellationToken);
        if (!exists) return null;

        var rows = await _dbContext.JobEvents
            .AsNoTracking()
            .Where(e => e.ReportId == id && e.OrganizationId == organizationId)
            .OrderByDescending(e => e.CreatedAt)
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(ToEventResponse).ToArray();
    }

    public Task<JobReportResponse?> UpdateAsync(Guid id, Guid organizationId, UpdateJobRequest request, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.update", token => UpdateAsyncCoreAsync(id, organizationId, request, token), cancellationToken);

    private async Task<JobReportResponse?> UpdateAsyncCoreAsync(Guid id, Guid organizationId, UpdateJobRequest request, CancellationToken cancellationToken)
    {
        if (organizationId != _currentUser.OrganizationId)
            return null;

        _dbContext.ChangeTracker.Clear();

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existing = await _dbContext.JobReports
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == organizationId, cancellationToken);

        if (existing is null || !JobStatusPolicy.CanEdit(ParseStatus(existing.Status)))
            return null;

        var now = DateTimeOffset.UtcNow;

        var customerId = existing.CustomerId;
        if (request.Customer?.Email is not null)
            customerId = await UpsertCustomerAsync(organizationId, request.Customer, cancellationToken);

        var entry = _dbContext.Entry(existing);
        entry.Property(e => e.CustomerId).CurrentValue = customerId;
        if (request.ReportNumber is not null) entry.Property(e => e.ReportNumber).CurrentValue = request.ReportNumber;
        if (request.ReportDate is not null) entry.Property(e => e.ReportDate).CurrentValue = ToDateTime(request.ReportDate);
        if (request.TaskDescription is not null) entry.Property(e => e.TaskDescription).CurrentValue = request.TaskDescription;
        entry.Property(e => e.CustomerObservations).CurrentValue = request.CustomerObservations;
        entry.Property(e => e.TechnicalObservations).CurrentValue = request.TechnicalObservations;
        if (request.InstallationTypes is not null) entry.Property(e => e.InstallationTypesJson).CurrentValue = ToJson(request.InstallationTypes);
        var normalizedWorkKind = NormalizeOptional(request.WorkKind);
        if (normalizedWorkKind is not null) entry.Property(e => e.WorkKind).CurrentValue = normalizedWorkKind;
        entry.Property(e => e.CustomWorkKind).CurrentValue = request.CustomWorkKind;
        entry.Property(e => e.Remarks).CurrentValue = request.Remarks;
        if (request.ClosureFlags is not null) entry.Property(e => e.ClosureFlagsJson).CurrentValue = ToJson(request.ClosureFlags);
        entry.Property(e => e.UpdatedAt).CurrentValue = now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (request.ControlInstallationTypes is not null)
        {
            await _dbContext.JobControlSubcategoryDecisions
                .Where(s => s.ReportId == id && s.OrganizationId == organizationId)
                .ExecuteDeleteAsync(cancellationToken);

            await ReplaceControlInstallationTypesAsync(organizationId, id, request.ControlInstallationTypes, now, cancellationToken);
        }

        await InsertEventAsync(organizationId, id, null, "updated", ToJsonNode(existing), ToJsonNode(request), now, cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return await GetAsync(id, organizationId, cancellationToken);
    }

    public Task<JobReportResponse?> TransitionAsync(Guid id, Guid organizationId, JobStatus nextStatus, Guid? actorId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.transition", token => TransitionAsyncCoreAsync(id, organizationId, nextStatus, actorId, token), cancellationToken);

    private async Task<JobReportResponse?> TransitionAsyncCoreAsync(Guid id, Guid organizationId, JobStatus nextStatus, Guid? actorId, CancellationToken cancellationToken)
    {
        if (organizationId != _currentUser.OrganizationId)
            return null;

        _dbContext.ChangeTracker.Clear();

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existing = await _dbContext.JobReports
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == organizationId, cancellationToken);

        if (existing is null) return null;

        var currentStatus = ParseStatus(existing.Status);
        if (!JobStatusPolicy.CanTransition(currentStatus, nextStatus))
            return null;

        var now = DateTimeOffset.UtcNow;
        var entry = _dbContext.Entry(existing);
        entry.Property(e => e.Status).CurrentValue = nextStatus.ToString();
        entry.Property(e => e.UpdatedAt).CurrentValue = now;
        if (nextStatus == JobStatus.Submitted)
            entry.Property(e => e.SubmittedAt).CurrentValue = now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await InsertEventAsync(organizationId, id, actorId, nextStatus.ToString().ToLowerInvariant(), ToJsonNode(existing), ToJsonNode(new { status = nextStatus.ToString() }), now, cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return await GetAsync(id, organizationId, cancellationToken);
    }

    public Task<JobReportResponse?> DeleteAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.delete", token => DeleteAsyncCoreAsync(id, organizationId, token), cancellationToken);

    private async Task<JobReportResponse?> DeleteAsyncCoreAsync(Guid id, Guid organizationId, CancellationToken cancellationToken)
    {
        if (organizationId != _currentUser.OrganizationId)
            return null;

        _dbContext.ChangeTracker.Clear();

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existing = await _dbContext.JobReports
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == organizationId, cancellationToken);

        if (existing is null) return null;

        if (existing.IsSoftDeleted || existing.DeletionScheduledAt.HasValue)
        {
            await tx.CommitAsync(cancellationToken);
            return await GetAsync(id, organizationId, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var deletionScheduledAt = now.Add(DeletionRetentionPeriod);

        var entry = _dbContext.Entry(existing);
        entry.Property(e => e.IsSoftDeleted).CurrentValue = true;
        entry.Property(e => e.DeletionScheduledAt).CurrentValue = deletionScheduledAt;
        entry.Property(e => e.UpdatedAt).CurrentValue = now;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await InsertEventAsync(organizationId, id, null, "deletionScheduled", ToJsonNode(existing), ToJsonNode(new { deletionScheduledAt }), now, cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return await GetAsync(id, organizationId, cancellationToken);
    }

    public Task<JobReportResponse?> RestoreDeletionAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.restore-deletion", token => RestoreDeletionAsyncCoreAsync(id, organizationId, token), cancellationToken);

    private async Task<JobReportResponse?> RestoreDeletionAsyncCoreAsync(Guid id, Guid organizationId, CancellationToken cancellationToken)
    {
        if (organizationId != _currentUser.OrganizationId)
            return null;

        _dbContext.ChangeTracker.Clear();

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existing = await _dbContext.JobReports
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == organizationId, cancellationToken);

        if (existing is null) return null;

        if (!existing.IsSoftDeleted && !existing.DeletionScheduledAt.HasValue)
        {
            await tx.CommitAsync(cancellationToken);
            return await GetAsync(id, organizationId, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var entry = _dbContext.Entry(existing);
        entry.Property(e => e.IsSoftDeleted).CurrentValue = false;
        entry.Property(e => e.DeletionScheduledAt).CurrentValue = null;
        entry.Property(e => e.UpdatedAt).CurrentValue = now;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await InsertEventAsync(organizationId, id, null, "deletionRestored", ToJsonNode(existing), ToJsonNode(new { deletionScheduledAt = (DateTimeOffset?)null }), now, cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return await GetAsync(id, organizationId, cancellationToken);
    }

    public Task<int> PurgeDeletionScheduledBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.purge-scheduled-deletions", token => PurgeDeletionScheduledBeforeAsyncCoreAsync(cutoff, token), cancellationToken);

    private async Task<int> PurgeDeletionScheduledBeforeAsyncCoreAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var dueJobIds = await _dbContext.JobReports
            .AsNoTracking()
            .Where(r => r.DeletionScheduledAt != null && r.DeletionScheduledAt <= cutoff)
            .Select(r => r.Id)
            .ToArrayAsync(cancellationToken);

        if (dueJobIds.Length == 0)
        {
            await tx.CommitAsync(cancellationToken);
            return 0;
        }

        await _dbContext.JobReportLinks
            .Where(l => dueJobIds.Contains(l.SourceReportId) || dueJobIds.Contains(l.TargetReportId))
            .ExecuteDeleteAsync(cancellationToken);

        var deletedCount = await _dbContext.JobReports
            .Where(r => dueJobIds.Contains(r.Id) && r.DeletionScheduledAt != null && r.DeletionScheduledAt <= cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
        return deletedCount;
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

        var targetUserIds = NormalizeUserIds(userIds);
        var targetAssignedUsers = await LoadAssignedUsersByIdsAsync(organizationId, targetUserIds, cancellationToken);
        if (targetAssignedUsers.Count != targetUserIds.Length)
            return null;

        var existingAssignedUsers = await LoadAssignedUsersAsync(organizationId, jobId, cancellationToken);
        var existingUserIdSet = existingAssignedUsers.Select(u => u.Id).OrderBy(id => id).ToArray();
        var orderedTargetIds = targetUserIds.OrderBy(id => id).ToArray();
        if (existingUserIdSet.SequenceEqual(orderedTargetIds))
            return await GetAsync(jobId, organizationId, cancellationToken);

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

        return await GetAsync(jobId, organizationId, cancellationToken);
    }

    private async Task<Guid> UpsertCustomerAsync(Guid organizationId, CustomerInfo customer, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Customers
            .FirstOrDefaultAsync(c => c.OrganizationId == organizationId && c.Email == customer.Email, cancellationToken);

        if (existing is not null)
        {
            var entry = _dbContext.Entry(existing);
            entry.Property(e => e.Name).CurrentValue = customer.Name ?? string.Empty;
            entry.Property(e => e.Address).CurrentValue = customer.Address;
            entry.Property(e => e.ContactPerson).CurrentValue = customer.ContactPerson;
            entry.Property(e => e.Phone).CurrentValue = customer.Phone;
            entry.Property(e => e.UpdatedAt).CurrentValue = DateTimeOffset.UtcNow;
            return existing.Id;
        }

        var row = new CustomerRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = customer.Name ?? string.Empty,
            Address = customer.Address,
            Email = customer.Email,
            ContactPerson = customer.ContactPerson,
            Phone = customer.Phone,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _dbContext.Customers.Add(row);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    private async Task ReplaceControlInstallationTypesAsync(
        Guid organizationId, Guid reportId,
        IReadOnlyList<ControlInstallationTypeRequest>? installationTypes,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (installationTypes is null) return;

        foreach (var installationType in installationTypes)
        {
            foreach (var subcategory in installationType.Subcategories)
            {
                var subcategoryDecisionId = Guid.NewGuid();
                _dbContext.JobControlSubcategoryDecisions.Add(new JobControlSubcategoryRow
                {
                    Id = subcategoryDecisionId,
                    OrganizationId = organizationId,
                    ReportId = reportId,
                    InstallationTypeId = installationType.InstallationTypeId,
                    SubcategoryId = subcategory.SubcategoryId,
                    CreatedAt = now,
                    UpdatedAt = now
                });

                foreach (var check in subcategory.ControlChecks)
                {
                    _dbContext.JobControlChecks.Add(new JobControlCheckRow
                    {
                        Id = Guid.NewGuid(),
                        OrganizationId = organizationId,
                        ReportId = reportId,
                        SubcategoryDecisionId = subcategoryDecisionId,
                        InstallationTypeId = installationType.InstallationTypeId,
                        SubcategoryId = subcategory.SubcategoryId,
                        ItemId = check.ItemId,
                        Checked = check.Checked,
                        Note = check.Note,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
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

    private async Task ReplaceAssignedUsersAsync(
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

    private async Task<IReadOnlyList<JobLinkInfoResponse>> LoadLinksAsync(Guid organizationId, Guid reportId, CancellationToken cancellationToken)
    {
        var links = await _dbContext.JobReportLinks
            .AsNoTracking()
            .Where(l => l.OrganizationId == organizationId && (l.SourceReportId == reportId || l.TargetReportId == reportId))
            .ToListAsync(cancellationToken);

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

    private async Task<IReadOnlyList<AssignedUserResponse>> LoadAssignedUsersAsync(
        Guid organizationId, Guid reportId, CancellationToken cancellationToken) =>
        (await LoadAssignedUsersAsync(organizationId, [reportId], cancellationToken)).GetValueOrDefault(reportId) ?? [];

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<AssignedUserResponse>>> LoadAssignedUsersAsync(
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

    private async Task<IReadOnlyList<AssignedUserResponse>> LoadAssignedUsersByIdsAsync(
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

    private sealed class WorksheetEntryProjection
    {
        public DateTime WorkDate { get; init; }
        public decimal HoursWorked { get; init; }
        public string DisplayName { get; init; } = "";
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

    private sealed class JobTotalHoursProjection
    {
        public Guid JobId { get; init; }
        public decimal? TotalHours { get; init; }
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

    private static JobReportResponse ToResponse(
        JobReportRow row,
        CustomerRow? customer,
        IEnumerable<JobControlSubcategoryRow> subcategories,
        IEnumerable<JobControlCheckRow> checks,
        IReadOnlyList<JobLinkInfoResponse> links,
        IReadOnlyList<AssignedUserResponse> assignedUsers,
        IReadOnlyList<WorksheetUserGroupResponse> worksheetEntries,
        decimal? totalHours = null)
    {
        var checksBySubcategory = checks
            .GroupBy(check => check.SubcategoryDecisionId)
            .ToDictionary(g => g.Key, g => g.Select(check => new ControlCheckResponse(
                check.Id, check.ItemId, check.Checked, check.Note, check.CreatedAt, check.UpdatedAt)).ToArray() as IReadOnlyList<ControlCheckResponse>);

        var subcategoryResponses = subcategories.Select(s => new ControlSubcategoryResponse(
            s.Id, s.InstallationTypeId, s.SubcategoryId,
            checksBySubcategory.TryGetValue(s.Id, out var sc) ? sc : [],
            s.CreatedAt, s.UpdatedAt)).ToArray();

        var installationTypeResponses = subcategoryResponses
            .GroupBy(s => s.InstallationTypeId)
            .Select(g => new ControlInstallationTypeResponse(g.Key, g.ToArray()))
            .ToArray();

        return new(
            row.Id, row.OrganizationId,
            customer is not null ? new CustomerInfo(customer.Id, customer.Name, customer.Address, customer.Email, customer.ContactPerson, customer.Phone) : null,
            row.ReportNumber, ParseStatus(row.Status), ToDateOnly(row.ReportDate),
            row.TaskDescription, row.CustomerObservations, row.TechnicalObservations,
            FromJsonList(row.InstallationTypesJson), row.WorkKind, row.CustomWorkKind,
            row.Remarks, FromJsonList(row.ClosureFlagsJson),
            installationTypeResponses, links,
            row.CreatedAt, row.UpdatedAt, row.SubmittedAt,
            assignedUsers, worksheetEntries,
            row.IsSoftDeleted, row.DeletionScheduledAt, totalHours);
    }

    private static JobStatus ParseStatus(string status) => Enum.Parse<JobStatus>(status, ignoreCase: true);

    private static JobEventResponse ToEventResponse(JobEventRow row) => new(
        row.Id, row.ReportId, row.ActorId, row.EventType,
        ToJsonObject(row.BeforeJson), ToJsonObject(row.AfterJson), row.CreatedAt);

    private static JsonObject? ToJsonObject(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json) as JsonObject;

    private static DateTime? ToDateTime(DateOnly? value) =>
        value is null ? null : value.Value.ToDateTime(TimeOnly.MinValue);

    private static DateOnly? ToDateOnly(DateTime? value) =>
        value is null ? null : DateOnly.FromDateTime(value.Value);

    private static string ToJson<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static JsonObject ToJsonNode<T>(T value) =>
        JsonSerializer.SerializeToNode(value, JsonOptions)?.AsObject() ?? [];

    private static IReadOnlyList<string> FromJsonList(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<string>>(json, JsonOptions) ?? [];

    private static Guid[] NormalizeUserIds(IEnumerable<Guid> userIds) =>
        userIds.Where(id => id != Guid.Empty).Distinct().ToArray();
}
