using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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

    public EfOrganizationRepository(SqlDbContext dbContext, IDatabaseRetryPolicy retryPolicy, ICurrentUserContext currentUser)
    {
        _dbContext = dbContext;
        _retryPolicy = retryPolicy;
        _currentUser = currentUser;
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

        _dbContext.Organizations.Add(organization);
        _dbContext.Users.Add(user);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            return null;
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

    private async Task<OrganizationRow?> GetOrganizationAsyncCoreAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await _dbContext.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(organization => organization.Id == organizationId, cancellationToken);

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

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException && sqlException.Number is 2601 or 2627;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
