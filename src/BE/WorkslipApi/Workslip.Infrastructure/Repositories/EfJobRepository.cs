using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Application.Customers;
using Workslip.Application.Jobs;
using Workslip.Application.Worksheets;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Mappers;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfJobRepository : IJobRepository
{
    private readonly SqlDbContext _dbContext;
    private readonly IDatabaseRetryPolicy _retryPolicy;
    private readonly ICustomerRepository _customerRepository;
    private readonly IAssignmentRepository _assignmentRepo;
    private readonly IJobLinkRepository _linkRepo;
    private readonly IWorksheetRepository _worksheetRepo;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DeletionRetentionPeriod = TimeSpan.FromDays(30);

    public EfJobRepository(SqlDbContext dbContext, IDatabaseRetryPolicy retryPolicy, ICustomerRepository customerRepository, IAssignmentRepository assignmentRepo, IJobLinkRepository linkRepo, IWorksheetRepository worksheetRepo)
    {
        _dbContext = dbContext;
        _retryPolicy = retryPolicy;
        _customerRepository = customerRepository;
        _assignmentRepo = assignmentRepo;
        _linkRepo = linkRepo;
        _worksheetRepo = worksheetRepo;
    }

    public Task<JobReportResponse> CreateAsync(Guid organizationId, CreateJobRequest request, IReadOnlyList<Guid> assignedUserIds, Guid? actorId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.create", token => CreateAsyncCoreAsync(organizationId, request, assignedUserIds, actorId, token), cancellationToken);

    private async Task<JobReportResponse> CreateAsyncCoreAsync(Guid organizationId, CreateJobRequest request, IReadOnlyList<Guid> assignedUserIds, Guid? actorId, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var reportId = Guid.NewGuid();

        var customerId = request.Customer?.Email is not null
            ? (Guid?)await _customerRepository.UpsertCustomerAsync(organizationId, request.Customer, cancellationToken)
            : null;

        _dbContext.JobReports.Add(new JobReportRow
        {
            Id = reportId,
            OrganizationId = organizationId,
            CustomerId = customerId,
            ReportNumber = request.ReportNumber,
            Status = JobStatus.Draft.ToString(),
            ReportDate = ToDateTime(request.Observations?.ReportDate),
            TaskDescription = request.Observations?.TaskDescription,
            CustomerObservations = request.Observations?.CustomerObservations,
            TechnicalObservations = request.Observations?.TechnicalObservations,
            WorkKind = NormalizeOptional(request.Work?.WorkKind),
            CustomWorkKind = request.Work?.CustomWorkKind,
            Remarks = request.Work?.Remarks,
            ClosureFlagsJson = JobReportMapper.ToJson(request.Work?.ClosureFlags ?? []),
            CreatedAt = now,
            UpdatedAt = now
        });

        if (request.Work?.InstallationTypes?.Count > 0)
        {
            await AddSelectedInstallationsAsync(organizationId, reportId, request.Work.InstallationTypes, now, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var normalizedUserIds = assignedUserIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        await _assignmentRepo.ReplaceAssignedUsersAsync(organizationId, reportId, normalizedUserIds, actorId, now, cancellationToken);
        var assignedUsers = await _assignmentRepo.GetAssignedUsersByIdsAsync(organizationId, normalizedUserIds, cancellationToken);
        await InsertEventAsync(organizationId, reportId, actorId, "created", null, JobReportMapper.ToJsonNode(new { reportId, assignedUsers }), now, cancellationToken);

        await tx.CommitAsync(cancellationToken);

        var job = await GetSingleJobAsync(reportId, organizationId, cancellationToken);
        return job ?? null!;
    }

    public Task<IReadOnlyList<JobListItemResponse>> ListAsync(JobQuery query, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.list", token => ListAsyncCoreAsync(query, token), cancellationToken);

    private async Task<IReadOnlyList<JobListItemResponse>> ListAsyncCoreAsync(JobQuery query, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        var projected = await (
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
            }
        ).Skip(query.Offset).Take(query.Limit).AsNoTracking().ToListAsync(cancellationToken);

        var reportIds = projected.Select(x => x.Id).ToArray();
        var assignedUsersByReport = await _assignmentRepo.GetAssignedUsersByReportAsync(query.OrganizationId, reportIds, cancellationToken);
        var totalHoursByJob = await _worksheetRepo.GetTotalHoursByJobAsync(reportIds, cancellationToken);

        var installationTypesByReport = await _dbContext.JobReportInstallations
            .AsNoTracking()
            .Where(it => it.OrganizationId == query.OrganizationId && reportIds.Contains(it.JobReportId))
            .Include(it => it.InstallationTypeDefinition)
            .GroupBy(it => it.JobReportId)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.OrderBy(it => it.SortOrder).Select(it => it.InstallationTypeDefinition.Name).ToArray() as IReadOnlyList<string>,
                cancellationToken);

        return projected.Select(x =>
        {
            var customerInfo = x.CustId is not null
                ? new CustomerInfo(x.CustId.Value, x.CustName ?? "", x.CustAddress, x.CustEmail, x.CustContactPerson, x.CustPhone)
                : null;

            return new JobListItemResponse(
                x.Id, x.OrganizationId,
                customerInfo,
                x.ReportNumber, JobReportMapper.ParseStatus(x.Status), JobReportMapper.ToDateOnly(x.ReportDate),
                installationTypesByReport.GetValueOrDefault(x.Id) ?? [], x.WorkKind, x.CustomWorkKind,
                x.CreatedAt, x.UpdatedAt, x.SubmittedAt,
                assignedUsersByReport.GetValueOrDefault(x.Id) ?? [],
                x.IsSoftDeleted, x.DeletionScheduledAt,
                totalHoursByJob.GetValueOrDefault(x.Id));
        }).ToArray();
    }

    public Task<JobReportResponse?> GetSingleJobAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.get", token => GetSingleJobCoreAsync(id, organizationId, token), cancellationToken);

    private async Task<JobReportResponse?> GetSingleJobCoreAsync(Guid id, Guid organizationId, CancellationToken cancellationToken)
    {
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

        var links = await _linkRepo.GetLinkInfoAsync(organizationId, id, cancellationToken);
        var assignedUsers = (await _assignmentRepo.GetAssignedUsersByReportAsync(organizationId, [id], cancellationToken)).GetValueOrDefault(id) ?? [];
        var totalHours = await _worksheetRepo.GetTotalHoursByJobAsync([id], cancellationToken);
        var worksheetEntries = await _worksheetRepo.GetGroupedByJobAsync(id, cancellationToken);
        var installationTypes = await _dbContext.LoadInstallationTypesAsync(organizationId, id, cancellationToken);

        return JobReportMapper.ToResponse(row, customer, links, assignedUsers, worksheetEntries, installationTypes, totalHours.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<JobEventResponse>?> GetEventsAsync(Guid id, Guid organizationId, int limit, int offset, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.events", token => GetEventsAsyncCoreAsync(id, organizationId, limit, offset, token), cancellationToken);

    private async Task<IReadOnlyList<JobEventResponse>?> GetEventsAsyncCoreAsync(Guid id, Guid organizationId, int limit, int offset, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        var exists = await _dbContext.JobReports.AsNoTracking().AnyAsync(r => r.Id == id && r.OrganizationId == organizationId, cancellationToken);
        if (!exists) return null;

        var rows = await _dbContext.JobEvents
            .AsNoTracking()
            .Where(e => e.ReportId == id && e.OrganizationId == organizationId)
            .OrderByDescending(e => e.CreatedAt)
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(JobReportMapper.ToEventResponse).ToArray();
    }

    public Task<JobReportResponse?> UpdateAsync(Guid id, Guid organizationId, UpdateJobRequest request, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.update", token => UpdateAsyncCoreAsync(id, organizationId, request, token), cancellationToken);

    private async Task<JobReportResponse?> UpdateAsyncCoreAsync(Guid id, Guid organizationId, UpdateJobRequest request, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existing = await _dbContext.JobReports
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == organizationId, cancellationToken);

        if (existing is null || !JobStatusPolicy.CanEdit(JobReportMapper.ParseStatus(existing.Status)))
            return null;

        var now = DateTimeOffset.UtcNow;

        var customerId = existing.CustomerId;
        if (request.Customer?.Email is not null)
            customerId = await _customerRepository.UpsertCustomerAsync(organizationId, request.Customer, cancellationToken);

        var entry = _dbContext.Entry(existing);
        entry.Property(e => e.CustomerId).CurrentValue = customerId;
        if (request.ReportNumber is not null) entry.Property(e => e.ReportNumber).CurrentValue = request.ReportNumber;
        if (request.Observations?.ReportDate is not null) entry.Property(e => e.ReportDate).CurrentValue = ToDateTime(request.Observations.ReportDate);
        if (request.Observations?.TaskDescription is not null) entry.Property(e => e.TaskDescription).CurrentValue = request.Observations.TaskDescription;
        entry.Property(e => e.CustomerObservations).CurrentValue = request.Observations?.CustomerObservations;
        entry.Property(e => e.TechnicalObservations).CurrentValue = request.Observations?.TechnicalObservations;
        var normalizedWorkKind = NormalizeOptional(request.Work?.WorkKind);
        if (normalizedWorkKind is not null) entry.Property(e => e.WorkKind).CurrentValue = normalizedWorkKind;
        entry.Property(e => e.CustomWorkKind).CurrentValue = request.Work?.CustomWorkKind;
        entry.Property(e => e.Remarks).CurrentValue = request.Work?.Remarks;
        if (request.Work?.ClosureFlags is not null) entry.Property(e => e.ClosureFlagsJson).CurrentValue = JobReportMapper.ToJson(request.Work.ClosureFlags);
        entry.Property(e => e.UpdatedAt).CurrentValue = now;

        if (request.Work?.InstallationTypes is not null)
        {
            var existingInstallations = await _dbContext.JobReportInstallations
                .Where(it => it.JobReportId == id && it.OrganizationId == organizationId)
                .ToListAsync(cancellationToken);
            _dbContext.JobReportInstallations.RemoveRange(existingInstallations);

            await AddSelectedInstallationsAsync(organizationId, id, request.Work.InstallationTypes, now, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await InsertEventAsync(organizationId, id, null, "updated", JobReportMapper.ToJsonNode(existing), JobReportMapper.ToJsonNode(request), now, cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return await GetSingleJobAsync(id, organizationId, cancellationToken);
    }

    public Task<JobReportResponse?> TransitionAsync(Guid id, Guid organizationId, JobStatus nextStatus, Guid? actorId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.transition", token => TransitionAsyncCoreAsync(id, organizationId, nextStatus, actorId, token), cancellationToken);

    private async Task<JobReportResponse?> TransitionAsyncCoreAsync(Guid id, Guid organizationId, JobStatus nextStatus, Guid? actorId, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existing = await _dbContext.JobReports
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == organizationId, cancellationToken);

        if (existing is null) return null;

        var currentStatus = JobReportMapper.ParseStatus(existing.Status);
        if (!JobStatusPolicy.CanTransition(currentStatus, nextStatus))
            return null;

        var now = DateTimeOffset.UtcNow;
        var entry = _dbContext.Entry(existing);
        entry.Property(e => e.Status).CurrentValue = nextStatus.ToString();
        entry.Property(e => e.UpdatedAt).CurrentValue = now;
        if (nextStatus == JobStatus.Submitted)
            entry.Property(e => e.SubmittedAt).CurrentValue = now;

        await _dbContext.SaveChangesAsync(cancellationToken);

        await InsertEventAsync(organizationId, id, actorId, nextStatus.ToString().ToLowerInvariant(), JobReportMapper.ToJsonNode(existing), JobReportMapper.ToJsonNode(new { status = nextStatus.ToString() }), now, cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return await GetSingleJobAsync(id, organizationId, cancellationToken);
    }

    public Task<JobReportResponse?> DeleteAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.delete", token => DeleteAsyncCoreAsync(id, organizationId, token), cancellationToken);

    private async Task<JobReportResponse?> DeleteAsyncCoreAsync(Guid id, Guid organizationId, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existing = await _dbContext.JobReports
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == organizationId, cancellationToken);

        if (existing is null) return null;

        if (existing.IsSoftDeleted || existing.DeletionScheduledAt.HasValue)
        {
            await tx.CommitAsync(cancellationToken);
            return await GetSingleJobAsync(id, organizationId, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var deletionScheduledAt = now.Add(DeletionRetentionPeriod);

        var entry = _dbContext.Entry(existing);
        entry.Property(e => e.IsSoftDeleted).CurrentValue = true;
        entry.Property(e => e.DeletionScheduledAt).CurrentValue = deletionScheduledAt;
        entry.Property(e => e.UpdatedAt).CurrentValue = now;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await InsertEventAsync(organizationId, id, null, "deletionScheduled", JobReportMapper.ToJsonNode(existing), JobReportMapper.ToJsonNode(new { deletionScheduledAt }), now, cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return await GetSingleJobAsync(id, organizationId, cancellationToken);
    }

    public Task<JobReportResponse?> RestoreDeletionAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.restore-deletion", token => RestoreDeletionAsyncCoreAsync(id, organizationId, token), cancellationToken);

    private async Task<JobReportResponse?> RestoreDeletionAsyncCoreAsync(Guid id, Guid organizationId, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existing = await _dbContext.JobReports
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == organizationId, cancellationToken);

        if (existing is null) return null;

        if (!existing.IsSoftDeleted && !existing.DeletionScheduledAt.HasValue)
        {
            await tx.CommitAsync(cancellationToken);
            return await GetSingleJobAsync(id, organizationId, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var entry = _dbContext.Entry(existing);
        entry.Property(e => e.IsSoftDeleted).CurrentValue = false;
        entry.Property(e => e.DeletionScheduledAt).CurrentValue = null;
        entry.Property(e => e.UpdatedAt).CurrentValue = now;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await InsertEventAsync(organizationId, id, null, "deletionRestored", JobReportMapper.ToJsonNode(existing), JobReportMapper.ToJsonNode(new { deletionScheduledAt = (DateTimeOffset?)null }), now, cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return await GetSingleJobAsync(id, organizationId, cancellationToken);
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

    private async Task AddSelectedInstallationsAsync(
        Guid organizationId,
        Guid jobReportId,
        IReadOnlyList<CreateInstallationTypeRequest> installationRequests,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var definitionIds = installationRequests
            .Where(request => request is not null)
            .Select(request => request.Id)
            .Distinct()
            .ToArray();
        var definitions = await _dbContext.InstallationTypeDefinitions
            .AsNoTracking()
            .Where(definition => definition.OrganizationId == organizationId && definitionIds.Contains(definition.Id))
            .Include(definition => definition.Mappings)
            .ToDictionaryAsync(definition => definition.Id, cancellationToken);

        for (var installationIndex = 0; installationIndex < installationRequests.Count; installationIndex++)
        {
            var installationRequest = installationRequests[installationIndex];
            if (installationRequest is null || !definitions.TryGetValue(installationRequest.Id, out var definition))
            {
                continue;
            }

            var selectedInstallation = new JobReportInstallationRow
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                JobReportId = jobReportId,
                InstallationTypeDefinitionId = installationRequest.Id,
                SortOrder = installationIndex + 1
            };

            _dbContext.JobReportInstallations.Add(selectedInstallation);

            var mappingsByPair = definition.Mappings.ToDictionary(mapping => (mapping.ControlCategoryId, mapping.ControlPointId));
            var categories = installationRequest.Categories ?? [];
            for (var categoryIndex = 0; categoryIndex < categories.Count; categoryIndex++)
            {
                var categoryRequest = categories[categoryIndex];
                if (categoryRequest is null)
                {
                    continue;
                }

                var selectedCategory = new JobReportInstallationCategoryRow
                {
                    Id = Guid.NewGuid(),
                    JobReportInstallationId = selectedInstallation.Id,
                    ControlCategoryId = categoryRequest.Id,
                    SortOrder = categoryIndex + 1,
                    JobReportInstallation = selectedInstallation
                };

                _dbContext.JobReportInstallationCategories.Add(selectedCategory);

                var controlPoints = categoryRequest.ControlPoints ?? [];
                for (var controlPointIndex = 0; controlPointIndex < controlPoints.Count; controlPointIndex++)
                {
                    var controlPointRequest = controlPoints[controlPointIndex];
                    if (controlPointRequest is null)
                    {
                        continue;
                    }

                    mappingsByPair.TryGetValue((categoryRequest.Id, controlPointRequest.Id), out var mapping);

                    _dbContext.JobReportInstallationControlPoints.Add(new JobReportInstallationControlPointRow
                    {
                        JobReportInstallationCategoryId = selectedCategory.Id,
                        ControlPointId = controlPointRequest.Id,
                        SortOrder = controlPointRequest.SortOrder ?? mapping?.SortOrder ?? controlPointIndex + 1,
                        IsRequired = controlPointRequest.IsRequired ?? mapping?.IsRequired ?? false,
                        JobReportInstallationCategory = selectedCategory
                    });
                }
            }
        }
    }

    private static DateTime? ToDateTime(DateOnly? value) =>
        value is null ? null : value.Value.ToDateTime(TimeOnly.MinValue);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
