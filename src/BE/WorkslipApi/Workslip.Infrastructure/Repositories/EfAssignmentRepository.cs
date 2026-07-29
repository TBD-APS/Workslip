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
    private readonly IJobViewRepository _jobViewRepo;

    public EfAssignmentRepository(
        SqlDbContext dbContext,
        IDatabaseRetryPolicy retryPolicy,
        ICurrentUserContext currentUser,
        IWorksheetRepository worksheetRepo,
        IJobViewRepository jobViewRepo)
    {
        _dbContext = dbContext;
        _retryPolicy = retryPolicy;
        _currentUser = currentUser;
        _worksheetRepo = worksheetRepo;
        _jobViewRepo = jobViewRepo;
    }

    public Task AssignAsync(
        Guid jobId,
        Guid organizationId,
        IReadOnlyList<Guid> userIds,
        Guid? actorId,
        CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "jobs.assign",
            token => AssignAsyncCoreAsync(jobId, organizationId, userIds, actorId, token),
            cancellationToken);

    private async Task AssignAsyncCoreAsync(
        Guid jobId,
        Guid organizationId,
        IReadOnlyList<Guid> userIds,
        Guid? actorId,
        CancellationToken cancellationToken)
    {
        if (organizationId != _currentUser.OrganizationId)
        {
            return;
        }

        _dbContext.ChangeTracker.Clear();

        var existing = await _dbContext.JobReports.FirstOrDefaultAsync(
            report => report.Id == jobId && report.OrganizationId == organizationId,
            cancellationToken);
        if (existing is null)
        {
            return;
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var normalizedUserIds = userIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        var currentAssignments = await _dbContext.JobAssignments
            .Where(assignment => assignment.ReportId == jobId && assignment.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);

        var assignmentsToRemove = currentAssignments
            .Where(assignment => !normalizedUserIds.Contains(assignment.UserId))
            .ToArray();

        var existingUserIds = currentAssignments.Select(assignment => assignment.UserId).ToHashSet();
        var userIdsToAdd = normalizedUserIds
            .Where(userId => !existingUserIds.Contains(userId))
            .ToArray();

        _dbContext.JobAssignments.RemoveRange(assignmentsToRemove);

        foreach (var userId in userIdsToAdd)
        {
            _dbContext.JobAssignments.Add(new JobAssignmentRow
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                ReportId = jobId,
                UserId = userId,
                AssignedByUserId = actorId,
                AssignedAt = now
            });
        }

        if (assignmentsToRemove.Length > 0 || userIdsToAdd.Length > 0)
        {
            var entry = _dbContext.Entry(existing);
            entry.Property(report => report.UpdatedAt).CurrentValue = now;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public Task<IReadOnlyList<JobListItemResponse>> GetMyAssignedJobsAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "jobs.get-my-assigned",
            token => GetMyAssignedJobsAsyncCoreAsync(organizationId, userId, token),
            cancellationToken);

    private async Task<IReadOnlyList<JobListItemResponse>> GetMyAssignedJobsAsyncCoreAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (organizationId != _currentUser.OrganizationId)
        {
            return [];
        }

        var projected = await (
            from report in _dbContext.JobReports.AsNoTracking()
            join assignment in _dbContext.JobAssignments.AsNoTracking()
                on new { report.Id, report.OrganizationId }
                equals new { Id = assignment.ReportId, assignment.OrganizationId }
            join customer in _dbContext.Customers.AsNoTracking()
                on new { Id = report.CustomerId, report.OrganizationId }
                equals new { Id = (Guid?)customer.Id, customer.OrganizationId } into reportCustomerJoin
            from customer in reportCustomerJoin.DefaultIfEmpty()
            where report.OrganizationId == organizationId
                && assignment.UserId == userId
                && !report.IsSoftDeleted
            orderby report.UpdatedAt descending
            select new
            {
                report.Id,
                report.OrganizationId,
                CustId = report.CustomerId,
                CustName = customer.Name,
                CustAddress = customer.Address,
                CustEmail = customer.Email,
                CustContactPerson = customer.ContactPerson,
                CustPhone = customer.Phone,
                report.ReportNumber,
                report.Status,
                report.JobType,
                report.DestinationAddress,
                report.DestinationZipCode,
                report.DestinationCity,
                report.TaskDescription,
                report.ReportDate,
                WorkKind = report.WorkKindRow != null
                    ? new JobWorkKindResponse(
                        report.WorkKindRow.Id,
                        report.WorkKindRow.NormalizedLabel,
                        report.WorkKindRow.Label,
                        report.WorkKindRow.RequiresCustomWorkKind,
                        report.WorkKindRow.SortOrder,
                        report.CustomWorkKind)
                    : null,
                report.CreatedAt,
                report.UpdatedAt,
                report.IsSoftDeleted,
                report.DeletionScheduledAt,
                report.RejectionNote
            }).ToListAsync(cancellationToken);

        var reportIds = projected.Select(report => report.Id).ToArray();

        var assignedUsers = await (
            from assignment in _dbContext.JobAssignments.AsNoTracking()
            join user in _dbContext.Users.AsNoTracking()
                on new { assignment.UserId, OrganizationId = (Guid?)assignment.OrganizationId }
                equals new { UserId = user.Id, user.OrganizationId }
            where assignment.OrganizationId == organizationId
                && reportIds.Contains(assignment.ReportId)
            select new
            {
                assignment.ReportId,
                user.Id,
                user.DisplayName
            }).ToListAsync(cancellationToken);

        var assignedDictionary = assignedUsers
            .GroupBy(user => user.ReportId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(user => user.Id == _currentUser.UserId ? 0 : 1)
                    .Select(user => new AssignedUserResponse(user.Id, user.DisplayName))
                    .ToArray() as IReadOnlyList<AssignedUserResponse>);

        var installationTypesByReport = await _dbContext.JobReportInstallations
            .AsNoTracking()
            .Where(installation => installation.OrganizationId == organizationId
                && reportIds.Contains(installation.JobReportId))
            .Include(installation => installation.InstallationTypeDefinition)
            .GroupBy(installation => installation.JobReportId)
            .ToDictionaryAsync(
                group => group.Key,
                group => group.OrderBy(installation => installation.SortOrder)
                    .Select(installation => installation.InstallationTypeDefinition.Name)
                    .ToArray() as IReadOnlyList<string>,
                cancellationToken);

        var totalHoursByJob = await _worksheetRepo.GetTotalHoursByJobAsync(reportIds, cancellationToken);

        var seenJobIds = await _jobViewRepo.GetViewedJobIdsAsync(
            userId,
            reportIds,
            ["New"],
            cancellationToken);
        var seenSet = new HashSet<Guid>(seenJobIds);

        var rejectedSeenJobIds = await _jobViewRepo.GetViewedJobIdsAsync(
            userId,
            reportIds,
            ["RejectedAssignment"],
            cancellationToken);
        var rejectedSeenSet = new HashSet<Guid>(rejectedSeenJobIds);

        return projected.Select(report =>
        {
            var customerInfo = report.CustId is not null
                ? new CustomerInfo(
                    report.CustId.Value,
                    report.CustName ?? string.Empty,
                    report.CustAddress,
                    report.CustEmail,
                    report.CustContactPerson,
                    report.CustPhone)
                : null;

            var status = Enum.Parse<JobStatus>(report.Status, ignoreCase: true);
            var isNewRejection = status == JobStatus.Rejected && !rejectedSeenSet.Contains(report.Id);

            return new JobListItemResponse(
                report.Id,
                report.OrganizationId,
                customerInfo,
                report.ReportNumber,
                status,
                JobReportMapper.ToDateOnly(report.ReportDate),
                report.JobType,
                report.DestinationAddress,
                report.DestinationZipCode,
                report.DestinationCity,
                report.TaskDescription,
                installationTypesByReport.GetValueOrDefault(report.Id) ?? [],
                report.WorkKind,
                report.CreatedAt,
                report.UpdatedAt,
                assignedDictionary.GetValueOrDefault(report.Id) ?? [],
                report.IsSoftDeleted,
                report.DeletionScheduledAt,
                totalHoursByJob.GetValueOrDefault(report.Id),
                seenSet.Contains(report.Id),
                isNewRejection,
                report.RejectionNote);
        }).ToArray();
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<AssignedUserResponse>>> GetAssignedUsersByReportAsync(
        Guid organizationId,
        IEnumerable<Guid> reportIds,
        CancellationToken cancellationToken)
    {
        var normalizedIds = reportIds.Distinct().ToArray();
        if (normalizedIds.Length == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<AssignedUserResponse>>();
        }

        var rows = await (
            from assignment in _dbContext.JobAssignments.AsNoTracking()
            join user in _dbContext.Users.AsNoTracking()
                on new { OrganizationId = (Guid?)assignment.OrganizationId, Id = assignment.UserId }
                equals new { user.OrganizationId, user.Id }
            where assignment.OrganizationId == organizationId
                && normalizedIds.Contains(assignment.ReportId)
            select new { assignment.ReportId, user.Id, user.DisplayName }
        ).ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.ReportId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(row => row.Id == _currentUser.UserId ? 0 : 1)
                    .Select(row => new AssignedUserResponse(row.Id, row.DisplayName))
                    .ToArray() as IReadOnlyList<AssignedUserResponse>);
    }

    public async Task<IReadOnlyList<AssignedUserResponse>> GetAssignedUsersByIdsAsync(
        Guid organizationId,
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return [];
        }

        var rows = await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.OrganizationId == organizationId && userIds.Contains(user.Id))
            .Select(user => new { user.Id, user.DisplayName })
            .ToDictionaryAsync(user => user.Id, cancellationToken);

        return userIds
            .Where(rows.ContainsKey)
            .Select(userId => new AssignedUserResponse(userId, rows[userId].DisplayName))
            .ToArray();
    }

    public Task AddAssignedUsersAsync(
        Guid organizationId,
        Guid reportId,
        IReadOnlyList<Guid> userIds,
        Guid? actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
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

        return Task.CompletedTask;
    }

    private async Task<IReadOnlyList<AssignedUserResponse>> GetSingleAssignedUsersAsync(
        Guid organizationId,
        Guid reportId,
        CancellationToken cancellationToken) =>
        (await GetAssignedUsersByReportAsync(
            organizationId,
            [reportId],
            cancellationToken)).GetValueOrDefault(reportId) ?? [];
}
