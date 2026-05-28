using Dapper;
using Workslip.Application.Users;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Repositories;

public sealed class DapperUserRepository(ISqlConnectionFactory connectionFactory) : IUserRepository
{
    public async Task<UserDataRow?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = "SELECT Id, OrganizationId, Email, DisplayName, Phone, Role, CreatedAt, UpdatedAt FROM dbo.Users WHERE Id = @Id";

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<UserDataRow?>(sql, new { Id = id });
        return row != null ? MapToData(row) : null;
    }

    public async Task<UserDataRow?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        const string sql = "SELECT Id, OrganizationId, Email, DisplayName, Phone, Role, CreatedAt, UpdatedAt FROM dbo.Users WHERE Email = @Email";

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<UserDataRow?>(sql, new { Email = email });
        return row != null ? MapToData(row) : null;
    }

    public async Task<UserDataRow?> GetByExternalIdentityAsync(string? entraId, IReadOnlyCollection<string> emailCandidates, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (1) Id, OrganizationId, Email, DisplayName, Phone, EntraEmail, EntraId, Role, CreatedAt, UpdatedAt
            FROM dbo.Users
            WHERE (@EntraId IS NOT NULL AND EntraId = @EntraId)
               OR (@HasEmailCandidates = 1 AND (
                    LOWER(LTRIM(RTRIM(ISNULL(Email, '')))) IN @EmailCandidates
                    OR LOWER(LTRIM(RTRIM(ISNULL(EntraEmail, '')))) IN @EmailCandidates))
            ORDER BY
                CASE
                    WHEN @EntraId IS NOT NULL AND EntraId = @EntraId THEN 0
                    WHEN @HasEmailCandidates = 1 AND LOWER(LTRIM(RTRIM(ISNULL(EntraEmail, '')))) IN @EmailCandidates THEN 1
                    WHEN @HasEmailCandidates = 1 AND LOWER(LTRIM(RTRIM(ISNULL(Email, '')))) IN @EmailCandidates THEN 2
                    ELSE 3
                END,
                CreatedAt DESC
            """;

        var normalizedEmailCandidates = NormalizeEmailCandidates(emailCandidates);
        var sqlEmailCandidates = normalizedEmailCandidates.Length > 0 ? normalizedEmailCandidates : ["__no_email_candidate__"];

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync<UserDataRow?>(new CommandDefinition(
            sql,
            new
            {
                EntraId = NullIfWhiteSpace(entraId),
                HasEmailCandidates = normalizedEmailCandidates.Length > 0 ? 1 : 0,
                EmailCandidates = sqlEmailCandidates
            },
            cancellationToken: cancellationToken));
        return row != null ? MapToData(row) : null;
    }

    public async Task<IReadOnlyList<UserDataRow>> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT Id, OrganizationId, Email, DisplayName, Phone, Role, CreatedAt, UpdatedAt FROM dbo.Users WHERE OrganizationId = @OrganizationId ORDER BY CreatedAt DESC";

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<UserDataRow>(sql, new { OrganizationId = organizationId });
        return rows.Select(MapToData).ToList();
    }

    public async Task<int> GetCountByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT COUNT(*) FROM dbo.Users WHERE OrganizationId = @OrganizationId";

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<int>(sql, new { OrganizationId = organizationId });
    }

    public async Task<Guid> CreateAsync(UserDataRow user, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO dbo.Users (Id, OrganizationId, Email, DisplayName, Phone, EntraEmail, EntraId, Role, CreatedAt, UpdatedAt)
            VALUES (@Id, @OrganizationId, @Email, @DisplayName, @Phone, @EntraEmail, @EntraId, @Role, @CreatedAt, @UpdatedAt)";

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);

        var row = new UserDataRow
        {
            Id = user.Id,
            OrganizationId = user.OrganizationId,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Phone = user.Phone,
            EntraEmail = user.EntraEmail,
            EntraId = user.EntraId,
            Role = user.Role,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };

        await connection.ExecuteAsync(sql, row);
        return user.Id;
    }

    public async Task UpdateAsync(UserDataRow user, CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE dbo.Users
            SET DisplayName = @DisplayName, Phone = @Phone, Role = @Role, UpdatedAt = @UpdatedAt
            WHERE Id = @Id";

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var row = new UserDataRow
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            Phone = user.Phone,
            Role = user.Role,
            UpdatedAt = user.UpdatedAt
        };
        await connection.ExecuteAsync(sql, row);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = "DELETE FROM dbo.Users WHERE Id = @Id";

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { Id = id });
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

    private static UserDataRow MapToData(UserDataRow row) =>
        new()
        {
            Id = row.Id,
            OrganizationId = row.OrganizationId,
            Email = row.Email,
            DisplayName = row.DisplayName,
            Phone = row.Phone,
            EntraEmail = row.EntraEmail,
            EntraId = row.EntraId,
            Role = row.Role,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
}
