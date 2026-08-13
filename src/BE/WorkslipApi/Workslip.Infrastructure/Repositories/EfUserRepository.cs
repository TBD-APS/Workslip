using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfUserRepository : IUserRepository
{
    private readonly SqlDbContext _dbContext;
    private readonly ICurrentUserContext _currentUser;

    public EfUserRepository(SqlDbContext dbContext, ICurrentUserContext currentUser)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
    }

    public async Task<UserDataRow?> GetAuthenticatedActorAsync(Guid id, CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid actorId || actorId != id)
        {
            return null;
        }

        return await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<UserDataRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id && u.OrganizationId == _currentUser.OrganizationId, cancellationToken);
    }

    public async Task<UserDataRow?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
        await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    public async Task<UserDataRow?> GetByExternalIdentityAsync(string? entraId, IReadOnlyCollection<string> emailCandidates, CancellationToken cancellationToken)
    {
        var normalizedEmails = NormalizeEmailCandidates(emailCandidates);

        var matched = await _dbContext.Users
            .AsNoTracking()
            .Where(u =>
                (entraId != null && u.EntraId == entraId) ||
                (normalizedEmails.Length > 0 && (normalizedEmails.Contains(u.Email.Trim().ToLower()) || normalizedEmails.Contains(u.EntraEmail.Trim().ToLower()))))
            .Select(u => new
            {
                u.Id,
                u.OrganizationId,
                u.Email,
                u.DisplayName,
                u.Phone,
                u.EntraEmail,
                u.EntraId,
                u.Role,
                u.BillableHourlyRate,
                u.CreatedAt,
                u.UpdatedAt,
                MatchPriority = entraId != null && u.EntraId == entraId
                    ? 0
                    : normalizedEmails.Contains(u.EntraEmail.Trim().ToLower())
                        ? 1
                        : 2
            })
            .OrderBy(x => x.MatchPriority)
            .ThenByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (matched is null) return null;

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
            BillableHourlyRate = matched.BillableHourlyRate,
            CreatedAt = matched.CreatedAt,
            UpdatedAt = matched.UpdatedAt
        };
    }

    public async Task<IReadOnlyList<UserDataRow>> GetByOrganizationIdAsync(Guid organizationId, int limit, int offset, string? search, string? sortBy, string? sortDirection, CancellationToken cancellationToken)
    {
        var isSuperadmin = string.Equals(_currentUser.Role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase);
        var isCurrentOrganization = _currentUser.OrganizationId == organizationId;
        var query = _dbContext.Users
            .AsNoTracking()
            .Where(u => u.OrganizationId == organizationId
                && (isSuperadmin || (isCurrentOrganization && u.Role != Roles.Superadmin)));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(u =>
                (u.DisplayName != null && u.DisplayName.Contains(term)) ||
                (u.Email != null && u.Email.Contains(term)) ||
                (u.Phone != null && u.Phone.Contains(term)) ||
                (u.Role != null && u.Role.Contains(term)));
        }

        var orderedQuery = query.OrderBy(u => u.Id == _currentUser.UserId ? 0 : 1);

        IOrderedQueryable<UserDataRow> sorted = (sortBy, sortDirection) switch
        {
            ("displayName", "asc") => orderedQuery.ThenBy(u => u.DisplayName),
            ("displayName", "desc") => orderedQuery.ThenByDescending(u => u.DisplayName),
            ("email", "asc") => orderedQuery.ThenBy(u => u.Email),
            ("email", "desc") => orderedQuery.ThenByDescending(u => u.Email),
            ("role", "asc") => orderedQuery.ThenBy(u => u.Role),
            ("role", "desc") => orderedQuery.ThenByDescending(u => u.Role),
            _ => orderedQuery.ThenByDescending(u => u.CreatedAt)
        };

        return await sorted
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var isSuperadmin = string.Equals(_currentUser.Role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase);
        var isCurrentOrganization = _currentUser.OrganizationId == organizationId;
        return await _dbContext.Users.CountAsync(u => u.OrganizationId == organizationId
            && (isSuperadmin || (isCurrentOrganization && u.Role != Roles.Superadmin)), cancellationToken);
    }

    public async Task<Guid> CreateAsync(UserDataRow user, CancellationToken cancellationToken)
    {
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return user.Id;
    }

    public async Task UpdateAsync(UserDataRow user, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == user.Id && u.OrganizationId == _currentUser.OrganizationId, cancellationToken);

        if (existing is null)
            return;

        existing.DisplayName = user.DisplayName;
        existing.Phone = user.Phone;
        existing.Role = user.Role;
        existing.BillableHourlyRate = user.BillableHourlyRate;
        existing.UpdatedAt = user.UpdatedAt;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AssignedJobResponse>> GetAssignedJobsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        var query =
            from a in _dbContext.JobAssignments.AsNoTracking()
            join r in _dbContext.JobReports.AsNoTracking()
                on new { a.ReportId, a.OrganizationId } equals new { ReportId = r.Id, r.OrganizationId }
            join c in _dbContext.Customers.AsNoTracking()
                on r.CustomerId equals (Guid?)c.Id into customerJoin
            from c in customerJoin.DefaultIfEmpty()
            where a.OrganizationId == organizationId && a.UserId == userId && !r.IsSoftDeleted
            orderby r.UpdatedAt descending
            select new AssignedJobResponse(
                r.Id,
                r.ReportNumber,
                r.Status,
                r.UpdatedAt,
                c.Name,
                c.Email,
                c.Address);

        return await query.ToListAsync(cancellationToken);
    }

    public async Task<decimal?> GetTotalHoursAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken)
    {
        return await _dbContext.Worksheets
            .AsNoTracking()
            .Where(w => w.OrganizationId == organizationId && w.UserId == userId)
            .SumAsync(w => (decimal?)w.HoursWorked, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, UserPeriodHours>> GetPeriodHoursAsync(Guid organizationId, DateOnly biweeklyStart, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var weekStart = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var biweeklyStartDt = biweeklyStart.ToDateTime(TimeOnly.MinValue);
        var weekStartDt = weekStart.ToDateTime(TimeOnly.MinValue);
        var monthStartDt = monthStart.ToDateTime(TimeOnly.MinValue);

        var data = await _dbContext.Worksheets
            .AsNoTracking()
            .Where(w => w.OrganizationId == organizationId && w.WorkDate >= biweeklyStartDt)
            .GroupBy(w => w.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                HoursThisWeek = g.Where(w => w.WorkDate >= weekStartDt).Sum(w => (decimal?)w.HoursWorked),
                HoursThisMonth = g.Where(w => w.WorkDate >= monthStartDt).Sum(w => (decimal?)w.HoursWorked),
                HoursBiweekly = g.Sum(w => (decimal?)w.HoursWorked),
            })
            .ToDictionaryAsync(x => x.UserId, x => new UserPeriodHours(
                x.HoursThisWeek ?? 0m,
                x.HoursThisMonth ?? 0m,
                x.HoursBiweekly ?? 0m), cancellationToken);

        return data;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == id && u.OrganizationId == _currentUser.OrganizationId, cancellationToken);

        if (user is null)
            return;

        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
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
