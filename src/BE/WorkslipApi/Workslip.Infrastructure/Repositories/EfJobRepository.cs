using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Application.Customers;
using Workslip.Application.Jobs;
using Workslip.Domain;
using Workslip.Domain.Models;
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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DeletionRetentionPeriod = TimeSpan.FromDays(30);

    public EfJobRepository(SqlDbContext dbContext, IDatabaseRetryPolicy retryPolicy, ICustomerRepository customerRepository, IAssignmentRepository assignmentRepo, IJobLinkRepository linkRepo)
    {
        _dbContext = dbContext;
        _retryPolicy = retryPolicy;
        _customerRepository = customerRepository;
        _assignmentRepo = assignmentRepo;
        _linkRepo = linkRepo;
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
            ClosureFlagsJson = ToJson(request.Work?.ClosureFlags ?? []),
            CreatedAt = now,
            UpdatedAt = now
        });

        if (request.Work?.InstallationTypes?.Count > 0)
        {
            var definitions = await _dbContext.InstallationTypeDefinitions
                .AsNoTracking()
                .Where(d => d.OrganizationId == organizationId)
                .Include(d => d.Mappings)
                .ToListAsync(cancellationToken);
            var defsByName = definitions.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var req in request.Work.InstallationTypes)
            {
                var installation = new InstallationTypeRow
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    Name = req.Name,
                    JobReportId = reportId,
                    CreatedAt = now
                };

                _dbContext.InstallationTypeRow.Add(installation);

                if (defsByName.TryGetValue(req.Name, out var def))
                {
                    foreach (var catReq in req.Categories ?? new List<CreateInstallationTypeCategoryRequest>())
                    {
                        var points = catReq.ControlPoints ?? [];
                        foreach (var cpReq in points)
                        {
                            _dbContext.InstallationControlPointsRow.Add(new InstallationControlPointRow
                            {
                                InstallationTypeId = installation.Id,
                                ControlCategoryId = catReq.Id,
                                ControlPointId = cpReq.Id,
                                SortOrder = cpReq.SortOrder ?? 0,
                                IsRequired = cpReq.IsRequired ?? false
                            });
                        }
                    }
                }
                else if (defsByName.TryGetValue(req.Name, out var def1))
                {
                    installation.SortOrder = def1.SortOrder;
                    foreach (var mapping in def1.Mappings)
                    {
                        _dbContext.InstallationControlPointsRow.Add(new InstallationControlPointRow
                        {
                            InstallationTypeId = installation.Id,
                            ControlCategoryId = mapping.ControlCategoryId,
                            ControlPointId = mapping.ControlPointId,
                            SortOrder = mapping.SortOrder,
                            IsRequired = mapping.IsRequired,
                            InstallationType = installation
                        });
                    }
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var normalizedUserIds = assignedUserIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        await _assignmentRepo.ReplaceAssignedUsersAsync(organizationId, reportId, normalizedUserIds, actorId, now, cancellationToken);
        var assignedUsers = await _assignmentRepo.GetAssignedUsersByIdsAsync(organizationId, normalizedUserIds, cancellationToken);
        await InsertEventAsync(organizationId, reportId, actorId, "created", null, ToJsonNode(new { reportId, assignedUsers }), now, cancellationToken);

        await tx.CommitAsync(cancellationToken);

        var job = await GetSingleJobAsync(reportId, organizationId, cancellationToken);
        return job ?? null;
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
        var totalHoursByJob = await GetTotalHoursByJobAsync(reportIds, cancellationToken);

        var installationTypesByReport = await _dbContext.InstallationTypeRow
            .AsNoTracking()
            .Where(it => it.OrganizationId == query.OrganizationId && reportIds.Contains(it.JobReportId))
            .GroupBy(it => it.JobReportId)
            .ToDictionaryAsync(
                g => g.Key,
                g => g.OrderBy(it => it.SortOrder).Select(it => it.Name).ToArray() as IReadOnlyList<string>,
                cancellationToken);

        return projected.Select(x =>
        {
            var customerInfo = x.CustId is not null
                ? new CustomerInfo(x.CustId.Value, x.CustName ?? "", x.CustAddress, x.CustEmail, x.CustContactPerson, x.CustPhone)
                : null;

            return new JobListItemResponse(
                x.Id, x.OrganizationId,
                customerInfo,
                x.ReportNumber, ParseStatus(x.Status), ToDateOnly(x.ReportDate),
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
        var assignedUsers = (await _assignmentRepo.GetAssignedUsersByReportAsync(organizationId, [id], cancellationToken)).GetValueOrDefault(id) ?? [];
        var totalHours = await GetTotalHoursByJobAsync([id], cancellationToken);
        var worksheetEntries = await GetWorksheetEntriesByJobAsync(id, cancellationToken);

        return ToResponse(row, customer, links, assignedUsers, worksheetEntries, totalHours.GetValueOrDefault(id));
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

        return rows.Select(ToEventResponse).ToArray();
    }

    public Task<JobReportResponse?> UpdateAsync(Guid id, Guid organizationId, UpdateJobRequest request, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.update", token => UpdateAsyncCoreAsync(id, organizationId, request, token), cancellationToken);

    private async Task<JobReportResponse?> UpdateAsyncCoreAsync(Guid id, Guid organizationId, UpdateJobRequest request, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existing = await _dbContext.JobReports
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == organizationId, cancellationToken);

        if (existing is null || !JobStatusPolicy.CanEdit(ParseStatus(existing.Status)))
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
        if (request.Work?.ClosureFlags is not null) entry.Property(e => e.ClosureFlagsJson).CurrentValue = ToJson(request.Work.ClosureFlags);
        entry.Property(e => e.UpdatedAt).CurrentValue = now;

        if (request.Work?.InstallationTypes is not null)
        {
            var existingInstallations = await _dbContext.InstallationTypeRow
                .Where(it => it.JobReportId == id && it.OrganizationId == organizationId)
                .ToListAsync(cancellationToken);
            _dbContext.InstallationTypeRow.RemoveRange(existingInstallations);

            var definitions = await _dbContext.InstallationTypeDefinitions
                .AsNoTracking()
                .Where(d => d.OrganizationId == organizationId)
                .Include(d => d.Mappings)
                .ToListAsync(cancellationToken);
            var defsByName = definitions.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var req in request.Work.InstallationTypes.Where(i => !string.IsNullOrWhiteSpace(i.Name)))
            {
                var name = req.Name.Trim();
                var it = new InstallationTypeRow
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    Name = name,
                    JobReportId = id,
                    CreatedAt = now
                };

                _dbContext.InstallationTypeRow.Add(it);

                if (req.Categories is not null && req.Categories.Count > 0)
                {
                    foreach (var catReq in req.Categories)
                    {
                        var points = catReq.ControlPoints ?? [];
                        foreach (var cpReq in points)
                        {
                            _dbContext.InstallationControlPointsRow.Add(new InstallationControlPointRow
                            {
                                InstallationTypeId = it.Id,
                                ControlCategoryId = catReq.Id,
                                ControlPointId = cpReq.Id,
                                SortOrder = cpReq.SortOrder ?? 0,
                                IsRequired = cpReq.IsRequired ?? false
                            });
                        }
                    }
                }
                else if (defsByName.TryGetValue(name, out var def))
                {
                    it.SortOrder = def.SortOrder;
                    foreach (var mapping in def.Mappings.OrderBy(m => m.SortOrder))
                    {
                        _dbContext.InstallationControlPointsRow.Add(new InstallationControlPointRow
                        {
                            InstallationTypeId = it.Id,
                            ControlCategoryId = mapping.ControlCategoryId,
                            ControlPointId = mapping.ControlPointId,
                            SortOrder = mapping.SortOrder,
                            IsRequired = mapping.IsRequired
                        });
                    }
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await InsertEventAsync(organizationId, id, null, "updated", ToJsonNode(existing), ToJsonNode(request), now, cancellationToken);
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

        await InsertEventAsync(organizationId, id, null, "deletionScheduled", ToJsonNode(existing), ToJsonNode(new { deletionScheduledAt }), now, cancellationToken);
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

        await InsertEventAsync(organizationId, id, null, "deletionRestored", ToJsonNode(existing), ToJsonNode(new { deletionScheduledAt = (DateTimeOffset?)null }), now, cancellationToken);
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
            row.ReportNumber, ParseStatus(row.Status), ToDateOnly(row.ReportDate),
            row.TaskDescription, row.CustomerObservations, row.TechnicalObservations,
            installationTypes, row.WorkKind, row.CustomWorkKind,
            row.Remarks, FromJsonList(row.ClosureFlagsJson), links,
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
}
