using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Workslip.Application.Users;
using Workslip.Domain;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfSuperAdminUserRepository(
    SqlDbContext dbContext,
    IDatabaseRetryPolicy retryPolicy) : ISuperAdminUserRepository
{
    public Task<IReadOnlyList<SuperAdminUserRecord>> ListAsync(
        int limit,
        int offset,
        string? search,
        string? sortBy,
        string? sortDirection,
        CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            "superadmin-users.list",
            token => ListCoreAsync(limit, offset, search, sortBy, sortDirection, token),
            cancellationToken);

    public Task<int> CountAsync(string? search, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            "superadmin-users.count",
            token => CountCoreAsync(search, token),
            cancellationToken);

    public Task<SuperAdminUserRecord?> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            "superadmin-users.get",
            token => GetCoreAsync(userId, token),
            cancellationToken);

    public Task<IReadOnlyList<SuperAdminFilialRecord>> ListFilialsAsync(CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            "superadmin-users.list-filials",
            ListFilialsCoreAsync,
            cancellationToken);

    public Task<bool> TenantFilialExistsAsync(
        Guid organizationId,
        Guid filialId,
        CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            "superadmin-users.validate-filial",
            token => TenantFilialExistsCoreAsync(organizationId, filialId, token),
            cancellationToken);

    public Task<UserDataRow?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            "superadmin-users.get-by-email",
            token => dbContext.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(user => user.Email == normalizedEmail, token),
            cancellationToken);

    public Task<Guid?> CreateAsync(UserDataRow user, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            "superadmin-users.create",
            token => CreateCoreAsync(user, token),
            cancellationToken);

    public Task<bool> UpdateAsync(
        Guid userId,
        string displayName,
        string phone,
        string role,
        Guid filialId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            "superadmin-users.update",
            token => UpdateCoreAsync(userId, displayName, phone, role, filialId, updatedAt, token),
            cancellationToken);

    public Task<SuperAdminUserDeleteStatus> DeleteAsync(Guid userId, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            "superadmin-users.delete",
            token => DeleteCoreAsync(userId, token),
            cancellationToken);

    public Task<bool> IsEntraIdentityReferencedAsync(string entraUserId, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync(
            "superadmin-users.entra-reference-exists",
            token => dbContext.Users
                .AsNoTracking()
                .AnyAsync(user => user.EntraId == entraUserId, token),
            cancellationToken);

    private async Task<IReadOnlyList<SuperAdminUserRecord>> ListCoreAsync(
        int limit,
        int offset,
        string? search,
        string? sortBy,
        string? sortDirection,
        CancellationToken cancellationToken)
    {
        var query = BuildQuery(search);
        query = (sortBy, sortDirection) switch
        {
            ("displayName", "asc") => query.OrderBy(user => user.DisplayName),
            ("displayName", "desc") => query.OrderByDescending(user => user.DisplayName),
            ("email", "asc") => query.OrderBy(user => user.Email),
            ("email", "desc") => query.OrderByDescending(user => user.Email),
            ("organization", "asc") => query.OrderBy(user => user.OrganizationName).ThenBy(user => user.DisplayName),
            ("organization", "desc") => query.OrderByDescending(user => user.OrganizationName).ThenBy(user => user.DisplayName),
            ("role", "asc") => query.OrderBy(user => user.Role).ThenBy(user => user.DisplayName),
            ("role", "desc") => query.OrderByDescending(user => user.Role).ThenBy(user => user.DisplayName),
            _ => query.OrderBy(user => user.OrganizationName).ThenBy(user => user.DisplayName)
        };

        return await query
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    private Task<int> CountCoreAsync(string? search, CancellationToken cancellationToken) =>
        BuildQuery(search).CountAsync(cancellationToken);

    private Task<SuperAdminUserRecord?> GetCoreAsync(Guid userId, CancellationToken cancellationToken) =>
        BuildQuery(search: null)
            .FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);

    private async Task<IReadOnlyList<SuperAdminFilialRecord>> ListFilialsCoreAsync(CancellationToken cancellationToken)
    {
        return await (
            from filial in dbContext.Set<OrganizationFilialRow>().AsNoTracking()
            join organization in dbContext.Organizations.AsNoTracking()
                on filial.OrganizationId equals organization.Id
            where organization.Id != PlatformOrganization.Id
            orderby organization.Name, filial.IsDefault descending, filial.Name
            select new SuperAdminFilialRecord(
                filial.Id,
                organization.Id,
                organization.Name,
                filial.Name,
                filial.IsDefault))
            .ToListAsync(cancellationToken);
    }

    private Task<bool> TenantFilialExistsCoreAsync(
        Guid organizationId,
        Guid filialId,
        CancellationToken cancellationToken) =>
        (
            from filial in dbContext.Set<OrganizationFilialRow>().AsNoTracking()
            join organization in dbContext.Organizations.AsNoTracking()
                on filial.OrganizationId equals organization.Id
            where organization.Id == organizationId
                && organization.Id != PlatformOrganization.Id
                && filial.Id == filialId
            select filial.Id)
        .AnyAsync(cancellationToken);

    private async Task<Guid?> CreateCoreAsync(UserDataRow user, CancellationToken cancellationToken)
    {
        if (user.OrganizationId == PlatformOrganization.Id || user.Role == Roles.Superadmin)
        {
            return null;
        }

        dbContext.Users.Add(user);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return user.Id;
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            dbContext.Entry(user).State = EntityState.Detached;
            return null;
        }
    }

    private async Task<bool> UpdateCoreAsync(
        Guid userId,
        string displayName,
        string phone,
        string role,
        Guid filialId,
        DateTimeOffset updatedAt,
        CancellationToken cancellationToken)
    {
        if (role == Roles.Superadmin)
        {
            return false;
        }

        var affectedRows = await dbContext.Users
            .Where(user => user.Id == userId && user.OrganizationId != PlatformOrganization.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.DisplayName, displayName)
                    .SetProperty(user => user.Phone, phone)
                    .SetProperty(user => user.Role, role)
                    .SetProperty(user => user.FilialId, filialId)
                    .SetProperty(user => user.UpdatedAt, updatedAt),
                cancellationToken);

        return affectedRows == 1;
    }

    private async Task<SuperAdminUserDeleteStatus> DeleteCoreAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == userId && candidate.OrganizationId != PlatformOrganization.Id,
                cancellationToken);
        if (user is null)
        {
            return SuperAdminUserDeleteStatus.NotFound;
        }

        var hasHistory = await dbContext.JobAssignments.AsNoTracking().AnyAsync(
                assignment => assignment.UserId == userId || assignment.AssignedByUserId == userId,
                cancellationToken)
            || await dbContext.JobEvents.AsNoTracking().AnyAsync(
                jobEvent => jobEvent.ActorId == userId,
                cancellationToken)
            || await dbContext.Worksheets.AsNoTracking().AnyAsync(
                worksheet => worksheet.UserId == userId,
                cancellationToken);
        if (hasHistory)
        {
            return SuperAdminUserDeleteStatus.HasHistory;
        }

        IDbContextTransaction? transaction = null;
        try
        {
            if (dbContext.Database.IsRelational())
            {
                transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
                await dbContext.JobViews
                    .Where(view => view.UserId == userId)
                    .ExecuteDeleteAsync(cancellationToken);
                await dbContext.PushSubscriptions
                    .Where(subscription => subscription.UserId == userId)
                    .ExecuteDeleteAsync(cancellationToken);
                await dbContext.NotificationQueue
                    .Where(notification => notification.UserId == userId)
                    .ExecuteDeleteAsync(cancellationToken);
                var deletedRows = await dbContext.Users
                    .Where(candidate => candidate.Id == userId && candidate.OrganizationId != PlatformOrganization.Id)
                    .ExecuteDeleteAsync(cancellationToken);
                if (deletedRows != 1)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return SuperAdminUserDeleteStatus.NotFound;
                }
                await transaction.CommitAsync(cancellationToken);
                return SuperAdminUserDeleteStatus.Deleted;
            }

            dbContext.JobViews.RemoveRange(dbContext.JobViews.Where(view => view.UserId == userId));
            dbContext.PushSubscriptions.RemoveRange(dbContext.PushSubscriptions.Where(subscription => subscription.UserId == userId));
            dbContext.NotificationQueue.RemoveRange(dbContext.NotificationQueue.Where(notification => notification.UserId == userId));
            var trackedUser = await dbContext.Users.FirstOrDefaultAsync(
                candidate => candidate.Id == userId && candidate.OrganizationId != PlatformOrganization.Id,
                cancellationToken);
            if (trackedUser is null)
            {
                return SuperAdminUserDeleteStatus.NotFound;
            }
            dbContext.Users.Remove(trackedUser);
            await dbContext.SaveChangesAsync(cancellationToken);
            return SuperAdminUserDeleteStatus.Deleted;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private IQueryable<SuperAdminUserRecord> BuildQuery(string? search)
    {
        var query =
            from user in dbContext.Users.AsNoTracking()
            join organization in dbContext.Organizations.AsNoTracking()
                on user.OrganizationId equals organization.Id
            join filial in dbContext.Set<OrganizationFilialRow>().AsNoTracking()
                on new { user.OrganizationId, Id = user.FilialId }
                equals new { filial.OrganizationId, filial.Id }
            where organization.Id != PlatformOrganization.Id
            select new SuperAdminUserRecord(
                user.Id,
                user.OrganizationId,
                organization.Name,
                user.FilialId,
                filial.Name,
                user.Email,
                user.DisplayName,
                user.Phone,
                user.Role,
                user.CreatedAt,
                user.UpdatedAt);

        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        var term = search.Trim();
        return query.Where(user =>
            user.DisplayName.Contains(term) ||
            user.Email.Contains(term) ||
            user.Phone.Contains(term) ||
            user.Role.Contains(term) ||
            user.OrganizationName.Contains(term) ||
            user.FilialName.Contains(term));
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException && sqlException.Number is 2601 or 2627;
}
