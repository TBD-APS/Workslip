using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Auth;
using Workslip.Application.Organizations;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class EfOrganizationRepository : IOrganizationRepository
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

    private async Task<bool> CvrExistsAsyncCoreAsync(string normalizedCvr, CancellationToken cancellationToken) =>
        await _dbContext.Organizations.AnyAsync(o => o.Cvr == normalizedCvr, cancellationToken);

    public Task<OrganizationRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("organizations.get_by_id", token => GetByIdAsyncCoreAsync(id, token), cancellationToken);

    private async Task<OrganizationRow?> GetByIdAsyncCoreAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id != _currentUser.OrganizationId)
            return null;

        return await _dbContext.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public Task<OrganizationOnboardingResponse?> CreateAsync(CreateOrganizationRequest request, string normalizedCvr, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("organizations.create", token => CreateAsyncCoreAsync(request, normalizedCvr, token), cancellationToken);

    private async Task<OrganizationOnboardingResponse?> CreateAsyncCoreAsync(CreateOrganizationRequest request, string normalizedCvr, CancellationToken cancellationToken)
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
            Email = NullIfWhiteSpace(request.AdminEmail) ?? "",
            Phone = NullIfWhiteSpace(request.AdminPhone) ?? "",
            Role = "Admin",
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
            new OrganizationUserResponse(userId, organizationId, request.AdminDisplayName.Trim(), NullIfWhiteSpace(request.AdminEmail), NullIfWhiteSpace(request.AdminPhone), "Admin", now, now));
    }

    public Task<CurrentUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken) =>
        _retryPolicy.ExecuteAsync("organizations.current_user", token => GetCurrentUserAsyncCoreAsync(userId, token), cancellationToken);

    private async Task<CurrentUserResponse?> GetCurrentUserAsyncCoreAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await (
            from u in _dbContext.Users.AsNoTracking()
            join o in _dbContext.Organizations.AsNoTracking() on u.OrganizationId equals o.Id
            where u.Id == userId && u.OrganizationId == _currentUser.OrganizationId && o.Id == _currentUser.OrganizationId
            select new
            {
                u.Id,
                u.DisplayName,
                u.Email,
                u.Phone,
                u.Role,
                OrgId = o.Id,
                OrganizationName = o.Name,
                OrganizationCvr = o.Cvr,
                OrganizationCreatedAt = o.CreatedAt,
                OrganizationUpdatedAt = o.UpdatedAt
            }).FirstOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new CurrentUserResponse(
                row.Id,
                row.DisplayName,
                row.Email,
                row.Phone,
                row.Role,
                new OrganizationResponse(row.OrgId, row.OrganizationName, row.OrganizationCvr, row.OrganizationCreatedAt, row.OrganizationUpdatedAt));
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException && sqlException.Number is 2601 or 2627;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
