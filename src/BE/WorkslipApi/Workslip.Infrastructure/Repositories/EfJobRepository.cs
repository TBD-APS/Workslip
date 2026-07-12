using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
    private const string DuplicateReportNumberIndexName = "UX_JobReports_Organization_ReportNumber";

    private readonly SqlDbContext _dbContext;
    private readonly IDatabaseRetryPolicy _retryPolicy;
    private readonly ICustomerRepository _customerRepository;
    private readonly IAssignmentRepository _assignmentRepo;
    private readonly IJobLinkRepository _linkRepo;
    private readonly IWorksheetRepository _worksheetRepo;

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
        
        // Generate sequential report number
        var nextSequenceNumber = await GetNextReportNumberAsync(organizationId, cancellationToken);
        var reportNumber = nextSequenceNumber.ToString("D4");

        var customerSnapshot = request.CustomerSnapshot;

        Guid? customerId = null;
        if (customerSnapshot is not null && (request.CustomerId is null || request.CreateCustomerFromSnapshot == true))
        {
            var customerInfo = new CustomerInfo(Guid.NewGuid(),
                                                customerSnapshot.Name,
                                                customerSnapshot.Address,
                                                customerSnapshot.Email,
                                                customerSnapshot.ContactPerson,
                                                customerSnapshot.Phone);

            customerId = await _customerRepository.CreateCustomerAsync(organizationId, customerInfo, cancellationToken);
        }

        var workKindLabel = NormalizeOptional(request.Work?.WorkKind);
        Guid? workKindId = null;
        if (workKindLabel is not null)
        {
            var matched = await _dbContext.JobWorkKinds
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.NormalizedLabel == workKindLabel, cancellationToken);
            workKindId = matched?.Id;
        }

        _dbContext.JobReports.Add(new JobReportRow
        {
            Id = reportId,
            OrganizationId = organizationId,
            CustomerId = customerId,
            CustomerName = customerSnapshot?.Name,
            CustomerEmail = customerSnapshot?.Email,
            CustomerPhone = customerSnapshot?.Phone,
            CustomerAddress = customerSnapshot?.Address,
            DestinationAddress = request.DestinationAddress,
            ReportNumber = reportNumber,
            Status = JobStatus.Draft.ToString(),
            JobType = Enum.TryParse<JobType>(request.JobType, out var jobType) ? jobType : JobType.Unknown,
            ReportDate = ToDateTime(request.Observations?.ReportDate),
            TaskDescription = request.Observations?.TaskDescription,
            CustomerObservations = request.Observations?.CustomerObservations,
            TechnicalObservations = request.Observations?.TechnicalObservations,
            WorkKindId = workKindId,
            CustomWorkKind = request.Work?.CustomWorkKind,
            Remarks = request.Work?.Remarks,
            CreatedAt = now,
            UpdatedAt = now
        });

        if (request.Work?.InstallationTypes?.Count > 0)
        {
            await AddSelectedInstallationsAsync(organizationId, reportId, request.Work.InstallationTypes, now, cancellationToken);
        }

        if (request.Work?.ClosureFlags?.Count > 0)
        {
            await AddClosureFlagsAsync(organizationId, reportId, request.Work.ClosureFlags, cancellationToken);
        }

        // Create timesheets if provided (before saving to ensure transaction atomicity)
        if (request.Timesheets?.Count > 0)
        {
            await CreateTimesheetsAsync(organizationId, reportId, request.Timesheets, now, cancellationToken);
        }

        var normalizedUserIds = assignedUserIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        await _assignmentRepo.AddAssignedUsersAsync(organizationId, reportId, normalizedUserIds, actorId, now, cancellationToken);
        
        await _dbContext.SaveChangesAsync(cancellationToken);        
        await tx.CommitAsync(cancellationToken);

        var job = await GetSingleJobAsync(reportId, organizationId, cancellationToken);
        return job!;
    }

private async Task CreateTimesheetsAsync(Guid organizationId, Guid jobReportId, IReadOnlyList<CreateTimesheetRequest> timesheets, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var worksheetRows = timesheets.Select(ts => new WorksheetRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            JobId = jobReportId,
            UserId = Guid.Parse(ts.UserId),
            WorkDate = DateOnly.Parse(ts.WorkDate).ToDateTime(TimeOnly.MinValue),
            HoursWorked = ts.HoursWorked,
            SleptOnJob = ts.SleptOnJob,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();

        _dbContext.Worksheets.AddRange(worksheetRows);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SyncTimesheetsAsync(Guid organizationId, Guid jobReportId, IReadOnlyList<CreateTimesheetRequest> timesheets, CancellationToken cancellationToken)
    {
        // Delete existing timesheets for this job
        var existingTimesheets = await _dbContext.Worksheets
            .Where(w => w.JobId == jobReportId && w.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);

        _dbContext.Worksheets.RemoveRange(existingTimesheets);

        // Add new timesheets
        var now = DateTimeOffset.UtcNow;
        var worksheetRows = timesheets.Select(ts => new WorksheetRow
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            JobId = jobReportId,
            UserId = Guid.Parse(ts.UserId),
            WorkDate = DateOnly.Parse(ts.WorkDate).ToDateTime(TimeOnly.MinValue),
            HoursWorked = ts.HoursWorked,
            SleptOnJob = ts.SleptOnJob,
            CreatedAt = now,
            UpdatedAt = now
        }).ToList();

        _dbContext.Worksheets.AddRange(worksheetRows);
    }

    public Task<JobListResponse> ListAsync(JobQuery query, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.list", token => ListAsyncCoreAsync(query, token), cancellationToken);

    private async Task<JobListResponse> ListAsyncCoreAsync(JobQuery query, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        var statuses = query.Statuses?.Select(x => x.ToString()).Distinct() ?? [];       

        var baseQuery =
            from job in _dbContext.JobReports.AsNoTracking()
            where job.OrganizationId == query.OrganizationId
            where statuses.Contains(job.Status)
            where job.IsSoftDeleted == false
            where query.ReportNumber == null || (job.ReportNumber != null && job.ReportNumber.Contains(query.ReportNumber))
            where query.CustomerName == null || (job.CustomerName != null && job.CustomerName.Contains(query.CustomerName))
            where query.CustomerEmail == null || (job.CustomerEmail != null && job.CustomerEmail.Contains(query.CustomerEmail))
            where query.CustomerAddress == null || (job.CustomerAddress != null && job.CustomerAddress.Contains(query.CustomerAddress))
            where query.Search == null || (
                (job.ReportNumber != null && job.ReportNumber.Contains(query.Search)) ||
                (job.CustomerName != null && job.CustomerName.Contains(query.Search)) ||
                (job.CustomerAddress != null && job.CustomerAddress.Contains(query.Search)) ||
                (job.CustomerEmail != null && job.CustomerEmail.Contains(query.Search))
            )
            select new
            {
                job.Id,
                job.OrganizationId,
                CustId = job.CustomerId,
                CustName = job.CustomerName ?? job.CustomerRow!.Name,
                CustAddress = job.CustomerAddress ?? job.CustomerRow!.Address,
                CustEmail = job.CustomerEmail ?? job.CustomerRow!.Email,
                CustContactPerson = job.CustomerContactPerson ?? job.CustomerRow!.ContactPerson,
                CustPhone = job.CustomerPhone ?? job.CustomerRow!.Phone,
                job.ReportNumber,
                job.Status,
                job.ReportDate,
                job.JobType,
                job.DestinationAddress,
                job.TaskDescription,
                WorkKind = job.WorkKindRow != null ? new JobWorkKindResponse(
                    job.WorkKindRow.Id,
                    job.WorkKindRow.NormalizedLabel,
                    job.WorkKindRow.Label,
                    job.WorkKindRow.RequiresCustomWorkKind,
                    job.WorkKindRow.SortOrder,
                    job.CustomWorkKind) : null,
                job.CreatedAt,
                job.UpdatedAt,
                job.IsSoftDeleted,
                job.DeletionScheduledAt
            };

        var totalCount = await baseQuery.CountAsync(cancellationToken);

        var ordered = (query.SortBy, query.SortDirection) switch
        {
            ("name", "asc") => baseQuery.OrderBy(j => j.CustName),
            ("name", _) => baseQuery.OrderByDescending(j => j.CustName),
            ("address", "asc") => baseQuery.OrderBy(j => j.CustAddress),
            ("address", _) => baseQuery.OrderByDescending(j => j.CustAddress),
            ("reportNumber", "asc") => baseQuery.OrderBy(j => j.ReportNumber),
            ("reportNumber", _) => baseQuery.OrderByDescending(j => j.ReportNumber),
            ("createdAt", "asc") => baseQuery.OrderBy(j => j.CreatedAt),
            ("createdAt", _) => baseQuery.OrderByDescending(j => j.CreatedAt),
            ("updatedAt", "asc") => baseQuery.OrderBy(j => j.UpdatedAt),
            ("updatedAt", _) => baseQuery.OrderByDescending(j => j.UpdatedAt),
            ("reportDate", "asc") => baseQuery.OrderBy(j => j.ReportDate),
            ("reportDate", _) => baseQuery.OrderByDescending(j => j.ReportDate),
            _ => baseQuery.OrderByDescending(j => j.UpdatedAt),
        };

        var projected = await ordered
            .Skip(query.Offset)
            .Take(query.Limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

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

        var items = projected.Select(x =>
        {
            var hasCustomerData = x.CustId is not null || !string.IsNullOrWhiteSpace(x.CustName);
            var customerInfo = hasCustomerData
                ? new CustomerInfo(x.CustId, x.CustName ?? "", x.CustAddress, x.CustEmail, x.CustContactPerson, x.CustPhone)
                : null;

            return new JobListItemResponse(
                x.Id, x.OrganizationId,
                customerInfo,
                x.ReportNumber, JobReportMapper.ParseStatus(x.Status), JobReportMapper.ToDateOnly(x.ReportDate),
                x.JobType,
                x.DestinationAddress,
                x.TaskDescription,
                installationTypesByReport.GetValueOrDefault(x.Id) ?? [], x.WorkKind,
                x.CreatedAt, x.UpdatedAt,
                assignedUsersByReport.GetValueOrDefault(x.Id) ?? [],
                x.IsSoftDeleted, x.DeletionScheduledAt,
                totalHoursByJob.GetValueOrDefault(x.Id));
        }).ToArray();

        return new JobListResponse(items, totalCount);
    }

    public Task<JobReportResponse?> GetSingleJobAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.get", token => GetSingleJobCoreAsync(id, organizationId, token), cancellationToken);

    private async Task<JobReportResponse?> GetSingleJobCoreAsync(Guid id, Guid organizationId, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        var row = await _dbContext.JobReports
            .Include(x => x.WorkKindRow)
            .Include(x => x.OrganizationRow)
            .Include(x => x.CustomerRow)
            .Include(x => x.ClosureFlags)
            .ThenInclude(jrcf => jrcf.ClosureFlag)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == organizationId, cancellationToken);

        if (row is null) 
            return null;


        var links = await _linkRepo.GetLinkInfoAsync(organizationId, id, cancellationToken);
        var assignedUsers = (await _assignmentRepo.GetAssignedUsersByReportAsync(organizationId, [id], cancellationToken)).GetValueOrDefault(id) ?? [];
        var totalHours = await _worksheetRepo.GetTotalHoursByJobAsync([id], cancellationToken);
        var worksheetEntries = await _worksheetRepo.GetGroupedByJobAsync(id, cancellationToken);
        var installationTypes = await _dbContext.LoadInstallationTypesAsync(organizationId, id, cancellationToken);
        var closureFlags = await _dbContext.LoadClosureFlagsAsync(organizationId, id, cancellationToken);

        return JobReportMapper.ToResponse(row, links, assignedUsers, worksheetEntries, installationTypes, closureFlags, totalHours.GetValueOrDefault(id));
    }

    public Task<IReadOnlyList<JobHistoryResponse>?> GetEventsAsync(Guid id, Guid organizationId, int limit, int offset, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.events", token => GetEventsAsyncCoreAsync(id, organizationId, limit, offset, token), cancellationToken);

    private async Task<IReadOnlyList<JobHistoryResponse>?> GetEventsAsyncCoreAsync(Guid id, Guid organizationId, int limit, int offset, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        var exists = await _dbContext.JobReports.AsNoTracking().AnyAsync(r => r.Id == id && r.OrganizationId == organizationId, cancellationToken);
        if (!exists) return null;

        var query = from e in _dbContext.JobEvents.AsNoTracking()
                    join u in _dbContext.Users.AsNoTracking()
                        on new { UserId = e.ActorId, e.OrganizationId }
                        equals new { UserId = (Guid?)u.Id, u.OrganizationId } into users
                    from u in users.DefaultIfEmpty()
                    where e.ReportId == id && e.OrganizationId == organizationId
                    orderby e.CreatedAt descending
                    select new { Row = e, ActorName = u != null ? u.DisplayName : null };

        var rows = await query
            .Skip(offset).Take(limit)
            .ToListAsync(cancellationToken);

        var response = rows.Select(e => JobReportMapper.ToHistoryResponse(e.Row, e.ActorName)).ToList();

        return response;
    }

    public Task<JobReportResponse?> UpdateAsync(Guid id, Guid organizationId, UpdateJobRequest request, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.update", token => UpdateAsyncCoreAsync(id, organizationId, request, token), cancellationToken);

    private async Task<JobReportResponse?> UpdateAsyncCoreAsync(Guid id, Guid organizationId, UpdateJobRequest request, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existing = await _dbContext.JobReports
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == organizationId, cancellationToken);

        if (existing is null)
            return null;

        var now = DateTimeOffset.UtcNow;

        var entry = _dbContext.Entry(existing);

        if (request.CustomerSnapshot is not null)
        {
            entry.Property(e => e.CustomerName).CurrentValue = ValueOrNull(request.CustomerSnapshot.Name);
            entry.Property(e => e.CustomerEmail).CurrentValue = ValueOrNull(request.CustomerSnapshot.Email);
            entry.Property(e => e.CustomerPhone).CurrentValue = ValueOrNull(request.CustomerSnapshot.Phone);
            entry.Property(e => e.CustomerAddress).CurrentValue = ValueOrNull(request.CustomerSnapshot.Address);
            entry.Property(e => e.CustomerContactPerson).CurrentValue = ValueOrNull(request.CustomerSnapshot.ContactPerson);
        }

        if (request.DestinationAddress is not null)
            entry.Property(e => e.DestinationAddress).CurrentValue = request.DestinationAddress;

        if (request.Observations is not null)
        {
            if (request.Observations.ReportDate is not null)
                entry.Property(e => e.ReportDate).CurrentValue = ToDateTime(request.Observations.ReportDate);

            entry.Property(e => e.TaskDescription).CurrentValue = request.Observations.TaskDescription;
            entry.Property(e => e.CustomerObservations).CurrentValue = request.Observations.CustomerObservations;
            entry.Property(e => e.TechnicalObservations).CurrentValue = request.Observations.TechnicalObservations;
        }

        if (request.Work is not null)
        {
            var normalizedWorkKind = NormalizeOptional(request.Work.WorkKind);
            if (normalizedWorkKind is not null)
            {
                var matched = await _dbContext.JobWorkKinds
                    .AsNoTracking()
                    .FirstOrDefaultAsync(w => w.NormalizedLabel == normalizedWorkKind, cancellationToken);
                entry.Property(e => e.WorkKindId).CurrentValue = matched?.Id;
            }
            entry.Property(e => e.CustomWorkKind).CurrentValue = request.Work.CustomWorkKind;
            entry.Property(e => e.Remarks).CurrentValue = request.Work.Remarks;

            if (request.Work.InstallationTypes is not null)
            {
                await SyncSelectedInstallationsAsync(organizationId, id, request.Work.InstallationTypes, cancellationToken);
            }

if (request.Work.ClosureFlags is not null)
            {
                await SyncClosureFlagsAsync(organizationId, id, request.Work.ClosureFlags, cancellationToken);
            }
        }

        // Update JobType if provided
        if (!string.IsNullOrWhiteSpace(request.JobType))
        {
            if (Enum.TryParse<JobType>(request.JobType, out var parsedJobType))
            {
                entry.Property(e => e.JobType).CurrentValue = parsedJobType;
            }
        }

        // Update timesheets if provided (replace all)
        if (request.Timesheets != null)
        {
            await SyncTimesheetsAsync(organizationId, id, request.Timesheets, cancellationToken);
        }

        entry.Property(e => e.UpdatedAt).CurrentValue = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
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

        if (existing is null)
            return null;

        var now = DateTimeOffset.UtcNow;
        var entry = _dbContext.Entry(existing);
        entry.Property(e => e.Status).CurrentValue = nextStatus.ToString();
        entry.Property(e => e.UpdatedAt).CurrentValue = now;

        if (nextStatus == JobStatus.InReview && existing.SubmittedAt is null)
        {
            entry.Property(e => e.SubmittedAt).CurrentValue = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return await GetSingleJobAsync(id, organizationId, cancellationToken);
    }

    public Task<JobDeleteRepositoryResult> DeleteAsync(Guid id, Guid organizationId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("jobs.delete", token => DeleteAsyncCoreAsync(id, organizationId, token), cancellationToken);

    private async Task<JobDeleteRepositoryResult> DeleteAsyncCoreAsync(Guid id, Guid organizationId, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        await using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var existingJob = await _dbContext.JobReports
            .FirstOrDefaultAsync(r => r.Id == id && r.OrganizationId == organizationId, cancellationToken);

        if (existingJob is null)
            return JobDeleteRepositoryResult.NotFound();

        var worksheetCount = await _dbContext.Worksheets
            .AsNoTracking()
            .CountAsync(w => w.JobId == id && w.OrganizationId == organizationId, cancellationToken);

        if (worksheetCount > 0)
            return JobDeleteRepositoryResult.BlockedByWorksheets(worksheetCount);

        var potentialLinks = await _dbContext.JobReportLinks
            .Where(l => (l.SourceReportId == id || l.TargetReportId == id) && l.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);

        if (potentialLinks.Count > 0)
            _dbContext.JobReportLinks.RemoveRange(potentialLinks);

        _dbContext.JobReports.Remove(existingJob);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return JobDeleteRepositoryResult.Deleted();
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


    private async Task SyncClosureFlagsAsync(
        Guid organizationId,
        Guid jobReportId,
        IReadOnlyList<string> normalizedLabels,
        CancellationToken cancellationToken)
    {
        var labels = normalizedLabels
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var flags = labels.Length == 0
            ? []
            : await _dbContext.JobClosureFlags
                .AsNoTracking()
                .Where(f => labels.Contains(f.NormalizedLabel))
                .ToListAsync(cancellationToken);
        var flagsByLabel = flags.ToDictionary(f => f.NormalizedLabel, StringComparer.OrdinalIgnoreCase);
        var requestedFlags = labels
            .Where(flagsByLabel.ContainsKey)
            .Select(label => flagsByLabel[label])
            .ToArray();
        var requestedFlagIds = requestedFlags.Select(flag => flag.Id).ToHashSet();

        var existingFlags = await _dbContext.JobReportClosureFlags
            .Where(flag => flag.JobReportId == jobReportId && flag.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);
        var existingByFlagId = existingFlags.ToDictionary(flag => flag.ClosureFlagId);

        var flagsToRemove = existingFlags
            .Where(flag => !requestedFlagIds.Contains(flag.ClosureFlagId))
            .ToArray();
        if (flagsToRemove.Length > 0)
            _dbContext.JobReportClosureFlags.RemoveRange(flagsToRemove);

        for (var sortOrder = 0; sortOrder < requestedFlags.Length; sortOrder++)
        {
            var flag = requestedFlags[sortOrder];
            if (existingByFlagId.TryGetValue(flag.Id, out var existingFlag))
            {
                _dbContext.Entry(existingFlag).Property(e => e.SortOrder).CurrentValue = sortOrder + 1;
                continue;
            }

            _dbContext.JobReportClosureFlags.Add(new JobReportClosureFlagRow
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                JobReportId = jobReportId,
                ClosureFlagId = flag.Id,
                SortOrder = sortOrder + 1
            });
        }
    }

    private async Task AddClosureFlagsAsync(
        Guid organizationId,
        Guid jobReportId,
        IReadOnlyList<string> normalizedLabels,
        CancellationToken cancellationToken)
    {
        var labels = normalizedLabels
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (labels.Length == 0) return;

        var flags = await _dbContext.JobClosureFlags
            .AsNoTracking()
            .Where(f => labels.Contains(f.NormalizedLabel))
            .ToListAsync(cancellationToken);

        var flagsByLabel = flags.ToDictionary(f => f.NormalizedLabel, StringComparer.OrdinalIgnoreCase);

        var sortOrder = 0;
        foreach (var label in labels)
        {
            if (!flagsByLabel.TryGetValue(label, out var flag)) continue;

            _dbContext.JobReportClosureFlags.Add(new JobReportClosureFlagRow
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                JobReportId = jobReportId,
                ClosureFlagId = flag.Id,
                SortOrder = ++sortOrder
            });
        }
    }

    private async Task SyncSelectedInstallationsAsync(
        Guid organizationId,
        Guid jobReportId,
        IReadOnlyList<CreateInstallationTypeRequest> installationRequests,
        CancellationToken cancellationToken)
    {
        var requested = installationRequests
            .Where(request => request is not null)
            .GroupBy(request => request.Id)
            .Select(group => group.First())
            .ToArray();

        var requestedDefinitionIds = requested.Select(request => request.Id).ToHashSet();
        var definitions = await _dbContext.InstallationTypeDefinitions
            .AsNoTracking()
            .Where(definition => definition.OrganizationId == organizationId && requestedDefinitionIds.Contains(definition.Id))
            .Include(definition => definition.Mappings)
            .ToDictionaryAsync(definition => definition.Id, cancellationToken);

        var existingInstallations = await _dbContext.JobReportInstallations
            .Where(installation => installation.JobReportId == jobReportId && installation.OrganizationId == organizationId)
            .Include(installation => installation.Categories)
            .ThenInclude(category => category.ControlPoints)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var existingByDefinitionId = existingInstallations.ToDictionary(installation => installation.InstallationTypeDefinitionId);
        var requestedValidDefinitionIds = requested
            .Where(request => definitions.ContainsKey(request.Id))
            .Select(request => request.Id)
            .ToHashSet();

        var installationsToRemove = existingInstallations
            .Where(installation => !requestedValidDefinitionIds.Contains(installation.InstallationTypeDefinitionId))
            .ToArray();
        if (installationsToRemove.Length > 0)
            _dbContext.JobReportInstallations.RemoveRange(installationsToRemove);

        for (var installationIndex = 0; installationIndex < requested.Length; installationIndex++)
        {
            var installationRequest = requested[installationIndex];
            if (!definitions.TryGetValue(installationRequest.Id, out var definition))
                continue;

            if (!existingByDefinitionId.TryGetValue(installationRequest.Id, out var installation))
            {
                installation = new JobReportInstallationRow
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = organizationId,
                    JobReportId = jobReportId,
                    InstallationTypeDefinitionId = installationRequest.Id,
                    SortOrder = installationIndex + 1
                };
                _dbContext.JobReportInstallations.Add(installation);
            }
            else
            {
                installation.SortOrder = installationIndex + 1;
            }

            SyncInstallationCategories(installation, definition, installationRequest.Categories ?? []);
        }
    }

    private void SyncInstallationCategories(
        JobReportInstallationRow installation,
        InstallationTypeDefinitionRow definition,
        IReadOnlyList<CreateInstallationTypeCategoryRequest> categoryRequests)
    {
        var requested = categoryRequests
            .Where(request => request is not null)
            .GroupBy(request => request.Id)
            .Select(group => group.First())
            .ToArray();
        var requestedCategoryIds = requested.Select(request => request.Id).ToHashSet();
        var existingCategories = installation.Categories.ToList();
        var existingByCategoryId = existingCategories.ToDictionary(category => category.ControlCategoryId);
        var mappingsByPair = definition.Mappings.ToDictionary(mapping => (mapping.ControlCategoryId, mapping.ControlPointId));
        var allowedCategoryIds = definition.Mappings.Select(mapping => mapping.ControlCategoryId).ToHashSet();

        var categoriesToRemove = existingCategories
            .Where(category => !requestedCategoryIds.Contains(category.ControlCategoryId))
            .ToArray();
        if (categoriesToRemove.Length > 0)
            _dbContext.JobReportInstallationCategories.RemoveRange(categoriesToRemove);

        for (var categoryIndex = 0; categoryIndex < requested.Length; categoryIndex++)
        {
            var categoryRequest = requested[categoryIndex];
            if (!allowedCategoryIds.Contains(categoryRequest.Id))
                continue;

            if (!existingByCategoryId.TryGetValue(categoryRequest.Id, out var category))
            {
                category = new JobReportInstallationCategoryRow
                {
                    Id = Guid.NewGuid(),
                    JobReportInstallationId = installation.Id,
                    ControlCategoryId = categoryRequest.Id,
                    JobReportInstallation = installation
                };
                _dbContext.JobReportInstallationCategories.Add(category);
                installation.Categories.Add(category);
            }

            category.SortOrder = categoryIndex + 1;
            category.IsIrrelevant = categoryRequest.IsIrrelevant ?? false;
            SyncInstallationControlPoints(category, mappingsByPair, categoryRequest.ControlPoints ?? []);
        }
    }

    private void SyncInstallationControlPoints(
        JobReportInstallationCategoryRow category,
        IReadOnlyDictionary<(Guid ControlCategoryId, Guid ControlPointId), InstallationTypeDefinitionMappingRow> mappingsByPair,
        IReadOnlyList<CreateInstallationTypeControlPointRequest> controlPointRequests)
    {
        var requested = controlPointRequests
            .Where(request => request is not null)
            .GroupBy(request => request.Id)
            .Select(group => group.First())
            .ToArray();
        var requestedControlPointIds = requested.Select(request => request.Id).ToHashSet();
        var existingControlPoints = category.ControlPoints.ToList();
        var existingByControlPointId = existingControlPoints.ToDictionary(controlPoint => controlPoint.ControlPointId);

        var controlPointsToRemove = existingControlPoints
            .Where(controlPoint => !requestedControlPointIds.Contains(controlPoint.ControlPointId))
            .ToArray();
        if (controlPointsToRemove.Length > 0)
            _dbContext.JobReportInstallationControlPoints.RemoveRange(controlPointsToRemove);

        for (var controlPointIndex = 0; controlPointIndex < requested.Length; controlPointIndex++)
        {
            var controlPointRequest = requested[controlPointIndex];
            if (!mappingsByPair.TryGetValue((category.ControlCategoryId, controlPointRequest.Id), out var mapping))
                continue;

            if (!existingByControlPointId.TryGetValue(controlPointRequest.Id, out var controlPoint))
            {
                controlPoint = new JobReportInstallationControlPointRow
                {
                    JobReportInstallationCategoryId = category.Id,
                    ControlPointId = controlPointRequest.Id,
                    JobReportInstallationCategory = category
                };
                _dbContext.JobReportInstallationControlPoints.Add(controlPoint);
                category.ControlPoints.Add(controlPoint);
            }

            controlPoint.SortOrder = controlPointRequest.SortOrder ?? mapping.SortOrder;
            controlPoint.IsRequired = controlPointRequest.IsRequired ?? mapping.IsRequired;
            controlPoint.IsChecked = controlPointRequest.IsChecked ?? false;
        }
    }

    private async Task<int> GetNextReportNumberAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var conn = _dbContext.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(cancellationToken);
        }

        var currentTransaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();

        await using (var lockCmd = conn.CreateCommand())
        {
            if (currentTransaction != null)
            {
                lockCmd.Transaction = currentTransaction;
            }

            lockCmd.CommandText = "SELECT Id FROM Organizations WITH (XLOCK, HOLDLOCK) WHERE Id = @orgId";
            var orgIdParam = lockCmd.CreateParameter();
            orgIdParam.ParameterName = "@orgId";
            orgIdParam.Value = organizationId;
            lockCmd.Parameters.Add(orgIdParam);

            // XLOCK = exclusive lock, HOLDLOCK = hold until end of transaction.
            // Forces sequential allocation per organization; reads still scale
            // because the lock is scoped to a single row.
            await lockCmd.ExecuteScalarAsync(cancellationToken);
        }

        // Re-read max under the lock; any concurrent caller for the same org
        // will block on the XLOCK above until our transaction commits/rolls back.
        var maxReportNumber = await _dbContext.JobReports
            .AsNoTracking()
            .Where(r => r.OrganizationId == organizationId)
            .Select(r => r.ReportNumber)
            .MaxAsync(cancellationToken);

        return ConvertToIntSafe(maxReportNumber) + 1;
    }

    private static int ConvertToIntSafe(string? reportNumber)
    {
        if (int.TryParse(reportNumber, out var result))
            return result;
        return 0;
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
                    IsIrrelevant = categoryRequest.IsIrrelevant ?? false,
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
                        IsChecked = controlPointRequest.IsChecked ?? false,
                        JobReportInstallationCategory = selectedCategory
                    });
                }
            }
        }
    }

    private static DateTime? ToDateTime(DateOnly? value) =>
        value?.ToDateTime(TimeOnly.MinValue);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? ValueOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

}
