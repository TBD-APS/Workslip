using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfUserRepository(SqlDbContext dbContext, ICurrentUserContext currentUser) : IUserRepository
{
    public async Task<UserDataRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var query = ScopeUserById(id, dbContext.Users.AsNoTracking());
        return await query.FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<UserDataRow?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
        await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Email == email, cancellationToken);

    public async Task<UserDataRow?> GetByExternalIdentityAsync(
        string? entraId,
        IReadOnlyCollection<string> emailCandidates,
        CancellationToken cancellationToken)
    {
        var normalizedEmails = NormalizeEmailCandidates(emailCandidates);

        var matched = await dbContext.Users
            .AsNoTracking()
            .Where(user =>
                (entraId != null && user.EntraId == entraId) ||
                (normalizedEmails.Length > 0 &&
                 (normalizedEmails.Contains(user.Email.Trim().ToLower()) ||
                  normalizedEmails.Contains(user.EntraEmail.Trim().ToLower()))))
            .Select(user => new
            {
                user.Id,
                user.OrganizationId,
                user.Email,
                user.DisplayName,
                user.Phone,
                user.EntraEmail,
                user.EntraId,
                user.Role,
                user.CreatedAt,
                user.UpdatedAt,
                MatchPriority = entraId != null && user.EntraId == entraId
                    ? 0
                    : normalizedEmails.Contains(user.EntraEmail.Trim().ToLower())
                        ? 1
                        : 2
            })
            .OrderBy(candidate => candidate.MatchPriority)
            .ThenByDescending(candidate => candidate.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (matched is null)
        {
            return null;
        }

        return new UserDataRow
        {
            Id = matched.Id,
            OrganizationId = matched.OrganizationId,
            Email = matched.Email,
            DisplayName = matched.DisplayName,
            Phone = matched.Phone,
            EntraEmail = matched.EntraEmail,
            EntraId = matched.EntraId,
            Role = matched.Role,
            CreatedAt = matched.CreatedAt,
            UpdatedAt = matched.UpdatedAt
        };
    }

    public async Task<IReadOnlyList<UserDataRow>> GetByOrganizationIdAsync(
        Guid organizationId,
        int limit,
        int offset,
        string? search,
        string? sortBy,
        string? sortDirection,
        CancellationToken cancellationToken)
    {
        var currentUserRole = currentUser.Role;
        var query = dbContext.Users
            .AsNoTracking()
            .Where(user => user.OrganizationId == organizationId
                && (currentUserRole == Roles.Superadmin || user.Role != Roles.Superadmin));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(user =>
                user.DisplayName.Contains(term) ||
                user.Email.Contains(term) ||
                user.Phone.Contains(term) ||
                user.Role.Contains(term));
        }

        var orderedQuery = query.OrderBy(user => user.Id == currentUser.UserId ? 0 : 1);
        IOrderedQueryable<UserDataRow> sorted = (sortBy, sortDirection) switch
        {
            ("displayName", "asc") => orderedQuery.ThenBy(user => user.DisplayName),
            ("displayName", "desc") => orderedQuery.ThenByDescending(user => user.DisplayName),
            ("email", "asc") => orderedQuery.ThenBy(user => user.Email),
            ("email", "desc") => orderedQuery.ThenByDescending(user => user.Email),
            ("role", "asc") => orderedQuery.ThenBy(user => user.Role),
            ("role", "desc") => orderedQuery.ThenByDescending(user => user.Role),
            _ => orderedQuery.ThenByDescending(user => user.CreatedAt)
        };

        return await sorted
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var currentUserRole = currentUser.Role;
        return await dbContext.Users.CountAsync(
            user => user.OrganizationId == organizationId
                && (currentUserRole == Roles.Superadmin || user.Role != Roles.Superadmin),
            cancellationToken);
    }

    public async Task<Guid> CreateAsync(UserDataRow user, CancellationToken cancellationToken)
    {
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return user.Id;
    }

    public async Task UpdateAsync(UserDataRow user, CancellationToken cancellationToken)
    {
        var existing = await ScopeUserById(user.Id, dbContext.Users)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is null)
        {
            return;
        }

        existing.DisplayName = user.DisplayName;
        existing.Phone = user.Phone;
        existing.Role = user.Role;
        existing.UpdatedAt = user.UpdatedAt;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AssignedJobResponse>> GetAssignedJobsAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var query =
            from assignment in dbContext.JobAssignments.AsNoTracking()
            join report in dbContext.JobReports.AsNoTracking()
                on new { assignment.ReportId, assignment.OrganizationId }
                equals new { ReportId = report.Id, report.OrganizationId }
            join customer in dbContext.Customers.AsNoTracking()
                on report.CustomerId equals (Guid?)customer.Id into customerJoin
            from customer in customerJoin.DefaultIfEmpty()
            where assignment.OrganizationId == organizationId
                && assignment.UserId == userId
                && !report.IsSoftDeleted
            orderby report.UpdatedAt descending
            select new AssignedJobResponse(
                report.Id,
                report.ReportNumber,
                report.Status,
                report.UpdatedAt,
                customer.Name,
                customer.Email,
                customer.Address);

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<decimal?> GetTotalHoursAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken) =>
        await dbContext.Worksheets
            .AsNoTracking()
            .Where(worksheet => worksheet.OrganizationId == organizationId && worksheet.UserId == userId)
            .SumAsync(worksheet => (decimal?)worksheet.HoursWorked, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, UserPeriodHours>> GetPeriodHoursAsync(
        Guid organizationId,
        DateOnly biweeklyStart,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var weekStart = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var biweeklyStartDt = biweeklyStart.ToDateTime(TimeOnly.MinValue);
        var weekStartDt = weekStart.ToDateTime(TimeOnly.MinValue);
        var monthStartDt = monthStart.ToDateTime(TimeOnly.MinValue);

        var data = await dbContext.Worksheets
            .AsNoTracking()
            .Where(worksheet => worksheet.OrganizationId == organizationId && worksheet.WorkDate >= biweeklyStartDt)
            .GroupBy(worksheet => worksheet.UserId)
            .Select(group => new
            {
                UserId = group.Key,
                HoursThisWeek = group.Where(worksheet => worksheet.WorkDate >= weekStartDt).Sum(worksheet => (decimal?)worksheet.HoursWorked),
                HoursThisMonth = group.Where(worksheet => worksheet.WorkDate >= monthStartDt).Sum(worksheet => (decimal?)worksheet.HoursWorked),
                HoursBiweekly = group.Sum(worksheet => (decimal?)worksheet.HoursWorked),
            })
            .ToDictionaryAsync(
                entry => entry.UserId,
                entry => new UserPeriodHours(
                    entry.HoursThisWeek ?? 0m,
                    entry.HoursThisMonth ?? 0m,
                    entry.HoursBiweekly ?? 0m),
                cancellationToken);

        return data;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await ScopeUserById(id, dbContext.Users)
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return;
        }

        dbContext.Users.Remove(user);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<UserDataRow> ScopeUserById(Guid id, IQueryable<UserDataRow> query)
    {
        if (currentUser.UserId == id)
        {
            return query.Where(user => user.Id == id);
        }

        var organizationId = currentUser.OrganizationId;
        return organizationId is null
            ? query.Where(_ => false)
            : query.Where(user => user.Id == id && user.OrganizationId == organizationId.Value);
    }

    private static string[] NormalizeEmailCandidates(IEnumerable<string> emailCandidates) =>
        emailCandidates
            .Select(candidate => NullIfWhiteSpace(candidate)?.ToLowerInvariant())
            .Where(candidate => candidate is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .ToArray();

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
