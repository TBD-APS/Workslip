using Dapper;
using Microsoft.Data.SqlClient;
using Workslip.Application.Organizations;
using Workslip.Infrastructure.Models;
using Workslip.Infrastructure.Resilience;

namespace Workslip.Infrastructure.Repositories;

public sealed class DapperOrganizationRepository(ISqlConnectionFactory connectionFactory, IDatabaseRetryPolicy retryPolicy) : IOrganizationRepository
{
    public Task<bool> CvrExistsAsync(string normalizedCvr, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("organizations.cvr_exists", token => CvrExistsAsyncCoreAsync(normalizedCvr, token), cancellationToken);

    private async Task<bool> CvrExistsAsyncCoreAsync(string normalizedCvr, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "select count(1) from dbo.Organizations where Cvr = @Cvr;",
            new { Cvr = normalizedCvr },
            cancellationToken: cancellationToken));

        return count > 0;
    }

    public Task<OrganizationOnboardingResponse?> CreateAsync(CreateOrganizationRequest request, string normalizedCvr, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("organizations.create", token => CreateAsyncCoreAsync(request, normalizedCvr, token), cancellationToken);

    private async Task<OrganizationOnboardingResponse?> CreateAsyncCoreAsync(CreateOrganizationRequest request, string normalizedCvr, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var now = DateTimeOffset.UtcNow;
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into dbo.Organizations (Id, Name, Cvr, CreatedAt, UpdatedAt)
                values (@Id, @Name, @Cvr, @CreatedAt, @UpdatedAt);
                """,
                new
                {
                    Id = organizationId,
                    Name = request.Name.Trim(),
                    Cvr = normalizedCvr,
                    CreatedAt = now,
                    UpdatedAt = now
                },
                transaction,
                cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into dbo.Users (Id, OrganizationId, DisplayName, Email, Phone, Role, CreatedAt, UpdatedAt)
                values (@Id, @OrganizationId, @DisplayName, @Email, @Phone, @Role, @CreatedAt, @UpdatedAt);
                """,
                new
                {
                    Id = userId,
                    OrganizationId = organizationId,
                    DisplayName = request.AdminDisplayName.Trim(),
                    Email = NullIfWhiteSpace(request.AdminEmail),
                    Phone = NullIfWhiteSpace(request.AdminPhone),
                    Role = "Admin",
                    CreatedAt = now,
                    UpdatedAt = now
                },
                transaction,
                cancellationToken: cancellationToken));

            transaction.Commit();
        }
        catch (SqlException exception) when (IsUniqueConstraintViolation(exception))
        {
            transaction.Rollback();
            return null;
        }

        return new OrganizationOnboardingResponse(
            new OrganizationResponse(organizationId, request.Name.Trim(), normalizedCvr, now, now),
            new OrganizationUserResponse(userId, organizationId, request.AdminDisplayName.Trim(), NullIfWhiteSpace(request.AdminEmail), NullIfWhiteSpace(request.AdminPhone), "Admin", now, now));
    }

    public Task<CurrentUserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken) =>
        retryPolicy.ExecuteAsync("organizations.current_user", token => GetCurrentUserAsyncCoreAsync(userId, token), cancellationToken);

    private async Task<CurrentUserResponse?> GetCurrentUserAsyncCoreAsync(Guid userId, CancellationToken cancellationToken)
    {
        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<CurrentUserRow>(new CommandDefinition(
            """
            select
                u.Id,
                u.OrganizationId,
                u.DisplayName,
                u.Email,
                u.Phone,
                u.Role,
                o.Id as OrganizationId,
                o.Name as OrganizationName,
                o.Cvr as OrganizationCvr,
                o.CreatedAt as OrganizationCreatedAt,
                o.UpdatedAt as OrganizationUpdatedAt
            from dbo.Users u
            inner join dbo.Organizations o on o.Id = u.OrganizationId
            where u.Id = @UserId;
            """,
            new { UserId = userId },
            cancellationToken: cancellationToken));

        return row is null
            ? null
            : new CurrentUserResponse(
                row.Id,
                row.DisplayName,
                row.Email,
                row.Phone,
                row.Role,
                new OrganizationResponse(row.OrganizationId, row.OrganizationName, row.OrganizationCvr, row.OrganizationCreatedAt, row.OrganizationUpdatedAt));
    }

    private static bool IsUniqueConstraintViolation(SqlException exception) =>
        exception.Number is 2601 or 2627;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class CurrentUserRow
    {
        public Guid Id { get; init; }
        public Guid OrganizationId { get; init; }
        public string DisplayName { get; init; } = "";
        public string? Email { get; init; }
        public string? Phone { get; init; }
        public string Role { get; init; } = "";
        public string OrganizationName { get; init; } = "";
        public string OrganizationCvr { get; init; } = "";
        public DateTimeOffset OrganizationCreatedAt { get; init; }
        public DateTimeOffset OrganizationUpdatedAt { get; init; }
    }
}
