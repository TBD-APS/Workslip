using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Application.Users;
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
            CreatedAt = matched.CreatedAt,
            UpdatedAt = matched.UpdatedAt
        };
    }

    public async Task<IReadOnlyList<UserDataRow>> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        return await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.OrganizationId == organizationId)
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        return await _dbContext.Users.CountAsync(u => u.OrganizationId == organizationId, cancellationToken);
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
