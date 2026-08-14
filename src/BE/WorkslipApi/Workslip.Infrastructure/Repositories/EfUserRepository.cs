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
        if (_currentUser.OrganizationId is not Guid organizationId)
        {
            return null;
        }

        if (IsCurrentActorSuperadmin())
        {
            return await _dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    u => u.Id == id && u.OrganizationId == organizationId,
                    cancellationToken);
        }

        var actorUserKind = await ResolveActorUserKindAsync(organizationId, cancellationToken);
        if (actorUserKind is null)
        {
            return null;
        }

        return await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.Id == id
                    && u.OrganizationId == organizationId
                    && u.Role != Roles.Superadmin
                    && u.UserKind == actorUserKind,
                cancellationToken);
    }

    public async Task<UserDataRow?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
        await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    public async Task<UserDataRow?> GetByExternalIdentityAsync(
        string? entraId,
        IReadOnlyCollection<string> emailCandidates,
        CancellationToken cancellationToken)
    {
        var normalizedEmails = NormalizeEmailCandidates(emailCandidates);

        var candidates = await _dbContext.Users
            .AsNoTracking()
            .Where(u =>
                (entraId != null && u.EntraId == entraId) ||
                (normalizedEmails.Length > 0 &&
                    (normalizedEmails.Contains(u.Email.Trim().ToLower()) ||
                     normalizedEmails.Contains(u.EntraEmail.Trim().ToLower()))))
            .Select(u => new
            {
                u.Id,
                u.OrganizationId,
                u.FilialId,
                u.Email,
                u.DisplayName,
                u.Phone,
                u.EntraEmail,
                u.EntraId,
                u.Role,
                u.UserKind,
                u.CreatedAt,
                u.UpdatedAt,
                MatchPriority = entraId != null && u.EntraId == entraId
                    ? 0
                    : normalizedEmails.Contains(u.EntraEmail.Trim().ToLower())
                        ? 1
                        : 2
            })
            .ToListAsync(cancellationToken);

        var matched = candidates
            .OrderBy(candidate => candidate.MatchPriority)
            .ThenByDescending(candidate => candidate.CreatedAt)
            .FirstOrDefault();

        if (matched is null)
        {
            return null;
        }

        return new UserDataRow
        {
            Id = matched.Id,
            OrganizationId = matched.OrganizationId,
            FilialId = matched.FilialId,
            Email = matched.Email,
            DisplayName = matched.DisplayName,
            Phone = matched.Phone,
            EntraEmail = matched.EntraEmail,
            EntraId = matched.EntraId,
            Role = matched.Role,
            UserKind = matched.UserKind,
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
        IQueryable<UserDataRow> query = _dbContext.Users
            .AsNoTracking()
            .Where(u => u.OrganizationId == organizationId);

        if (!IsCurrentActorSuperadmin())
        {
            if (_currentUser.OrganizationId != organizationId)
            {
                return Array.Empty<UserDataRow>();
            }

            var actorUserKind = await ResolveActorUserKindAsync(organizationId, cancellationToken);
            if (actorUserKind is null)
            {
                return Array.Empty<UserDataRow>();
            }

            query = query.Where(u => u.Role != Roles.Superadmin && u.UserKind == actorUserKind);
        }

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
        IQueryable<UserDataRow> query = _dbContext.Users
            .AsNoTracking()
            .Where(u => u.OrganizationId == organizationId);

        if (!IsCurrentActorSuperadmin())
        {
            if (_currentUser.OrganizationId != organizationId)
            {
                return 0;
            }

            var actorUserKind = await ResolveActorUserKindAsync(organizationId, cancellationToken);
            if (actorUserKind is null)
            {
                return 0;
            }

            query = query.Where(u => u.Role != Roles.Superadmin && u.UserKind == actorUserKind);
        }

        return await query.CountAsync(cancellationToken);
    }

    public async Task<Guid> CreateAsync(UserDataRow user, CancellationToken cancellationToken)
    {
        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return user.Id;
    }

    public async Task UpdateAsync(UserDataRow user, CancellationToken cancellationToken)
    {
        if (_currentUser.OrganizationId is not Guid organizationId)
        {
            return;
        }

        var isSuperadmin = IsCurrentActorSuperadmin();
        var actorUserKind = isSuperadmin
            ? null
            : await ResolveActorUserKindAsync(organizationId, cancellationToken);
        if (!isSuperadmin && actorUserKind is null)
        {
            return;
        }

        var existing = await _dbContext.Users
            .FirstOrDefaultAsync(
                u => u.Id == user.Id
                    && u.OrganizationId == organizationId
                    && (isSuperadmin
                        || (u.Role != Roles.Superadmin && u.UserKind == actorUserKind)),
                cancellationToken);

        if (existing is null)
        {
            return;
        }

        existing.DisplayName = user.DisplayName;
        existing.Phone = user.Phone;
        existing.Role = user.Role;
        existing.UpdatedAt = user.UpdatedAt;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AssignedJobResponse>> GetAssignedJobsAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
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

    public async Task<decimal?> GetTotalHoursAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.Worksheets
            .AsNoTracking()
            .Where(w => w.OrganizationId == organizationId && w.UserId == userId)
            .SumAsync(w => (decimal?)w.HoursWorked, cancellationToken);
    }

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
            .ToDictionaryAsync(
                x => x.UserId,
                x => new UserPeriodHours(
                    x.HoursThisWeek ?? 0m,
                    x.HoursThisMonth ?? 0m,
                    x.HoursBiweekly ?? 0m),
                cancellationToken);

        return data;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (_currentUser.OrganizationId is not Guid organizationId)
        {
            return;
        }

        var isSuperadmin = IsCurrentActorSuperadmin();
        var actorUserKind = isSuperadmin
            ? null
            : await ResolveActorUserKindAsync(organizationId, cancellationToken);
        if (!isSuperadmin && actorUserKind is null)
        {
            return;
        }

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(
                u => u.Id == id
                    && u.OrganizationId == organizationId
                    && (isSuperadmin
                        || (u.Role != Roles.Superadmin && u.UserKind == actorUserKind)),
                cancellationToken);

        if (user is null)
        {
            return;
        }

        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private bool IsCurrentActorSuperadmin() =>
        string.Equals(_currentUser.Role, Roles.Superadmin, StringComparison.OrdinalIgnoreCase);

    private async Task<string?> ResolveActorUserKindAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        if (_currentUser.UserId is not Guid actorId || _currentUser.OrganizationId != organizationId)
        {
            return null;
        }

        var userKind = await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == actorId && user.OrganizationId == organizationId)
            .Select(user => user.UserKind)
            .SingleOrDefaultAsync(cancellationToken);

        return UserKinds.Normalize(userKind);
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
