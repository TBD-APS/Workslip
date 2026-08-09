using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Workslip.Application.Auth;
using Workslip.Application.Organizations;
using Workslip.Domain.Models;
using Workslip.Domain;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfOrganizationRepository : IOrganizationRepository, IOrganizationAdministrationRepository
{
    private readonly SqlDbContext _dbContext;
    private readonly IDatabaseRetryPolicy _retryPolicy;
    private readonly ICurrentUserContext _currentUser;
    private readonly InstallationBaselineProvisioner _installationBaselineProvisioner;

    public EfOrganizationRepository(
        SqlDbContext dbContext,
        IDatabaseRetryPolicy retryPolicy,
        ICurrentUserContext currentUser,
        InstallationBaselineProvisioner installationBaselineProvisioner)
    {
        _dbContext = dbContext;
        _retryPolicy = retryPolicy;
        _currentUser = currentUser;
        _installationBaselineProvisioner = installationBaselineProvisioner;
    }

    public Task<bool> CvrExistsAsync(string normalizedCvr, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("organizations.cvr_exists", token => CvrExistsAsyncCoreAsync(normalizedCvr, token), cancellationToken);

    public Task<OrganizationRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("organizations.get_by_id", token => GetByIdAsyncCoreAsync(id, token), cancellationToken);

    public Task<OrganizationOnboardingResponse?> CreateAsync(
        CreateOrganizationRequest request,
        string normalizedCvr,
        CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("organizations.create", token => CreateAsyncCoreAsync(request, normalizedCvr, token), cancellationToken);

    public Task<CurrentUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("organizations.current_user", token => GetCurrentUserAsyncCoreAsync(userId, token), cancellationToken);

    public Task<IReadOnlyList<OrganizationRow>> ListOrganizationsAsync(CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "organization-admin.list-organizations",
            ListOrganizationsAsyncCoreAsync,
            cancellationToken);

    public Task<OrganizationRow?> GetOrganizationAsync(Guid organizationId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "organization-admin.get-organization",
            token => GetOrganizationAsyncCoreAsync(organizationId, token),
            cancellationToken);

    public Task<UserDataRow?> GetUserByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "organization-admin.get-user-by-email",
            token => GetUserByEmailAsyncCoreAsync(normalizedEmail, token),
            cancellationToken);

    public Task<UserDataRow?> GetUnlinkedAdminAsync(Guid organizationId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "organization-admin.get-unlinked-admin",
            token => GetUnlinkedAdminAsyncCoreAsync(organizationId, token),
            cancellationToken);

    public Task<bool> IsEntraIdentityReferencedAsync(string entraUserId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "organization-admin.entra-reference-exists",
            token => IsEntraIdentityReferencedAsyncCoreAsync(entraUserId, token),
            cancellationToken);

    public Task<Guid?> CreateAdminAsync(UserDataRow admin, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "organization-admin.create",
            token => CreateAdminAsyncCoreAsync(admin, token),
            cancellationToken);

    public Task<bool> UpdateAdminAsync(
        UserDataRow admin,
        string expectedEmail,
        string expectedEntraId,
        CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "organization-admin.update",
            token => UpdateAdminAsyncCoreAsync(admin, expectedEmail, expectedEntraId, token),
            cancellationToken);

    public Task<IReadOnlyList<OrganizationUserRow>> ListUsersAsync(
        Guid? organizationId,
        int limit,
        int offset,
        string? search,
        string? sortBy,
        string? sortDirection,
        CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "organization-admin.list-users",
            token => ListUsersAsyncCoreAsync(organizationId, limit, offset, search, sortBy, sortDirection, token),
            cancellationToken);

    public Task<int> CountUsersAsync(Guid? organizationId, string? search, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "organization-admin.count-users",
            token => CountUsersAsyncCoreAsync(organizationId, search, token),
            cancellationToken);

    public Task<OrganizationUserRow?> GetUserWithOrganizationAsync(Guid userId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "organization-admin.get-user",
            token => GetUserWithOrganizationAsyncCoreAsync(userId, token),
            cancellationToken);

    public Task<Guid?> CreateUserAsync(UserDataRow user, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "organization-admin.create-user",
            token => CreateUserAsyncCoreAsync(user, token),
            cancellationToken);

    public Task<bool> UpdateUserAsync(
        UserDataRow user,
        string expectedEmail,
        string expectedEntraId,
        CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "organization-admin.update-user",
            token => UpdateUserAsyncCoreAsync(user, expectedEmail, expectedEntraId, token),
            cancellationToken);

    public Task<bool> DeleteUserAsync(Guid userId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync(
            "organization-admin.delete-user",
            token => DeleteUserAsyncCoreAsync(userId, token),
            cancellationToken);

    private async Task<bool> CvrExistsAsyncCoreAsync(string normalizedCvr, CancellationToken cancellationToken) =>
        await _dbContext.Organizations.AnyAsync(organization => organization.Cvr == normalizedCvr, cancellationToken);

    private async Task<OrganizationRow?> GetByIdAsyncCoreAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id != _currentUser.OrganizationId)
        {
            return null;
        }

        return await _dbContext.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(organization => organization.Id == id, cancellationToken);
    }

    private async Task<OrganizationOnboardingResponse?> CreateAsyncCoreAsync(
        CreateOrganizationRequest request,
        string normalizedCvr,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var organization = new OrganizationRow
        {
            Id = organizationId,
            Name = request.Name.Trim(),
            Cvr = normalizedCvr,
            CreatedAt = now,
            UpdatedAt = now
        };

        var user = new UserDataRow
        {
            Id = userId,
            OrganizationId = organizationId,
            DisplayName = request.AdminDisplayName.Trim(),
            Email = NullIfWhiteSpace(request.AdminEmail)?.ToLowerInvariant() ?? string.Empty,
            Phone = NullIfWhiteSpace(request.AdminPhone) ?? string.Empty,
            Role = Roles.Admin,
            CreatedAt = now,
            UpdatedAt = now
        };

        IDbContextTransaction? transaction = null;
        try
        {
            if (_dbContext.Database.IsRelational())
            {
                transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            }

            // Persist Organization + default Filial first so the database-level
            // composite Filial FK is present before the creator is inserted.
            // The surrounding transaction keeps onboarding atomic.
            _dbContext.Organizations.Add(organization);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _dbContext.Users.Add(user);

            // Required tenant baseline is staged only during explicit organization onboarding.
            // Application startup never backfills or reconciles tenant reference data in production.
            await _installationBaselineProvisioner.ProvisionAsync(
                organizationId,
                cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return null;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }

        return new OrganizationOnboardingResponse(
            new OrganizationResponse(organizationId, request.Name.Trim(), normalizedCvr, now, now),
            new OrganizationUserResponse(
                userId,
                organizationId,
                request.AdminDisplayName.Trim(),
                NullIfWhiteSpace(request.AdminEmail),
                NullIfWhiteSpace(request.AdminPhone),
                Roles.Admin,
                EntraInvitationSent: false,
                now,
                now));
    }

    private async Task<CurrentUserResponse?> GetCurrentUserAsyncCoreAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await (
            from user in _dbContext.Users.AsNoTracking()
            join organization in _dbContext.Organizations.AsNoTracking()
                on user.OrganizationId equals organization.Id
            where user.Id == userId
                && user.OrganizationId == _currentUser.OrganizationId
                && organization.Id == _currentUser.OrganizationId
            select new
            {
                user.Id,
                user.DisplayName,
                user.Email,
                user.Phone,
                user.Role,
                OrganizationId = organization.Id,
                OrganizationName = organization.Name,
                OrganizationCvr = organization.Cvr,
                OrganizationCreatedAt = organization.CreatedAt,
                OrganizationUpdatedAt = organization.UpdatedAt
            }).FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new CurrentUserResponse(
                row.Id,
                row.DisplayName,
                row.Email,
                row.Phone,
                row.Role,
                new OrganizationResponse(
                    row.OrganizationId,
                    row.OrganizationName,
                    row.OrganizationCvr,
                    row.OrganizationCreatedAt,
                    row.OrganizationUpdatedAt));
    }

    private async Task<IReadOnlyList<OrganizationRow>> ListOrganizationsAsyncCoreAsync(CancellationToken cancellationToken) =>
        await _dbContext.Organizations
            .AsNoTracking()
            .Where(organization => organization.Id != PlatformOrganization.Id)
            .OrderBy(organization => organization.Name)
            .ThenBy(organization => organization.Cvr)
            .ToListAsync(cancellationToken);

    private async Task<OrganizationRow?> GetOrganizationAsyncCoreAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await _dbContext.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(
                organization =>
                    organization.Id == organizationId &&
                    organization.Id != PlatformOrganization.Id,
                cancellationToken);

    private async Task<UserDataRow?> GetUserByEmailAsyncCoreAsync(string normalizedEmail, CancellationToken cancellationToken) =>
        await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(user => user.Email == normalizedEmail, cancellationToken);

    private async Task<UserDataRow?> GetUnlinkedAdminAsyncCoreAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await _dbContext.Users
            .AsNoTracking()
            .Where(user => user.OrganizationId == organizationId
                && user.Role == Roles.Admin
                && user.Email == string.Empty
                && user.EntraId == string.Empty)
            .OrderBy(user => user.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<bool> IsEntraIdentityReferencedAsyncCoreAsync(string entraUserId, CancellationToken cancellationToken) =>
        await _dbContext.Users
            .AsNoTracking()
            .AnyAsync(user => user.EntraId == entraUserId, cancellationToken);

    private async Task<Guid?> CreateAdminAsyncCoreAsync(UserDataRow admin, CancellationToken cancellationToken)
    {
        _dbContext.Users.Add(admin);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return admin.Id;
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            _dbContext.Entry(admin).State = EntityState.Detached;
            return null;
        }
    }

    private async Task<bool> UpdateAdminAsyncCoreAsync(
        UserDataRow admin,
        string expectedEmail,
        string expectedEntraId,
        CancellationToken cancellationToken)
    {
        var affectedRows = await _dbContext.Users
            .Where(user => user.Id == admin.Id
                && user.OrganizationId == admin.OrganizationId
                && user.Email == expectedEmail
                && user.EntraId == expectedEntraId
                && user.Role != Roles.Superadmin)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.Email, admin.Email)
                    .SetProperty(user => user.DisplayName, admin.DisplayName)
                    .SetProperty(user => user.Phone, admin.Phone)
                    .SetProperty(user => user.EntraId, admin.EntraId)
                    .SetProperty(user => user.EntraEmail, admin.EntraEmail)
                    .SetProperty(user => user.Role, Roles.Admin)
                    .SetProperty(user => user.UpdatedAt, admin.UpdatedAt),
                cancellationToken);

        return affectedRows == 1;
    }

    private async Task<IReadOnlyList<OrganizationUserRow>> ListUsersAsyncCoreAsync(
        Guid? organizationId,
        int limit,
        int offset,
        string? search,
        string? sortBy,
        string? sortDirection,
        CancellationToken cancellationToken)
    {
        var query =
            from user in _dbContext.Users.AsNoTracking()
            join organization in _dbContext.Organizations.AsNoTracking()
                on user.OrganizationId equals organization.Id
            where organization.Id != PlatformOrganization.Id
                && (organizationId == null || user.OrganizationId == organizationId)
            select new { user, organization.Name };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(row =>
                row.user.DisplayName.Contains(term) ||
                row.user.Email.Contains(term) ||
                row.user.Phone.Contains(term) ||
                row.user.Role.Contains(term) ||
                row.Name.Contains(term));
        }

        var sorted = (sortBy, sortDirection) switch
        {
            ("displayName", "asc") => query.OrderBy(row => row.user.DisplayName),
            ("displayName", "desc") => query.OrderByDescending(row => row.user.DisplayName),
            ("email", "asc") => query.OrderBy(row => row.user.Email),
            ("email", "desc") => query.OrderByDescending(row => row.user.Email),
            ("role", "asc") => query.OrderBy(row => row.user.Role),
            ("role", "desc") => query.OrderByDescending(row => row.user.Role),
            ("organizationName", "asc") => query.OrderBy(row => row.Name),
            ("organizationName", "desc") => query.OrderByDescending(row => row.Name),
            _ => query.OrderByDescending(row => row.user.CreatedAt)
        };

        var rows = await sorted
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(row => new OrganizationUserRow(row.user, row.Name)).ToList();
    }

    private async Task<int> CountUsersAsyncCoreAsync(Guid? organizationId, string? search, CancellationToken cancellationToken)
    {
        var query =
            from user in _dbContext.Users.AsNoTracking()
            join organization in _dbContext.Organizations.AsNoTracking()
                on user.OrganizationId equals organization.Id
            where organization.Id != PlatformOrganization.Id
                && (organizationId == null || user.OrganizationId == organizationId)
            select new { user, organization.Name };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(row =>
                row.user.DisplayName.Contains(term) ||
                row.user.Email.Contains(term) ||
                row.user.Phone.Contains(term) ||
                row.user.Role.Contains(term) ||
                row.Name.Contains(term));
        }

        return await query.CountAsync(cancellationToken);
    }

    private async Task<OrganizationUserRow?> GetUserWithOrganizationAsyncCoreAsync(Guid userId, CancellationToken cancellationToken) =>
        await (
            from user in _dbContext.Users.AsNoTracking()
            join organization in _dbContext.Organizations.AsNoTracking()
                on user.OrganizationId equals organization.Id
            where user.Id == userId && organization.Id != PlatformOrganization.Id
            select new OrganizationUserRow(user, organization.Name))
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<Guid?> CreateUserAsyncCoreAsync(UserDataRow user, CancellationToken cancellationToken)
    {
        _dbContext.Users.Add(user);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return user.Id;
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            _dbContext.Entry(user).State = EntityState.Detached;
            return null;
        }
    }

    private async Task<bool> UpdateUserAsyncCoreAsync(
        UserDataRow user,
        string expectedEmail,
        string expectedEntraId,
        CancellationToken cancellationToken)
    {
        var affectedRows = await _dbContext.Users
            .Where(candidate => candidate.Id == user.Id
                && candidate.OrganizationId == user.OrganizationId
                && candidate.OrganizationId != PlatformOrganization.Id
                && candidate.Email == expectedEmail
                && candidate.EntraId == expectedEntraId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.DisplayName, user.DisplayName)
                    .SetProperty(candidate => candidate.Phone, user.Phone)
                    .SetProperty(candidate => candidate.Role, user.Role)
                    .SetProperty(candidate => candidate.UpdatedAt, user.UpdatedAt),
                cancellationToken);

        return affectedRows == 1;
    }

    private async Task<bool> DeleteUserAsyncCoreAsync(Guid userId, CancellationToken cancellationToken)
    {
        var affectedRows = await _dbContext.Users
            .Where(user => user.Id == userId && user.OrganizationId != PlatformOrganization.Id)
            .ExecuteDeleteAsync(cancellationToken);

        return affectedRows == 1;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException && sqlException.Number is 2601 or 2627;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
