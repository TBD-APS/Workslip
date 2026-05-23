using Dapper;
using Workslip.Application.Users;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Repositories;

public sealed class DapperUserRepository(ISqlConnectionFactory connectionFactory) : IUserRepository
{
    public async Task<UserData?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = "SELECT Id, OrganizationId, Email, DisplayName, Phone, Role, CreatedAt, UpdatedAt FROM dbo.Users WHERE Id = @Id";

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<UserRow?>(sql, new { Id = id });
        return row != null ? MapToData(row) : null;
    }

    public async Task<UserData?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        const string sql = "SELECT Id, OrganizationId, Email, DisplayName, Phone, Role, CreatedAt, UpdatedAt FROM dbo.Users WHERE Email = @Email";

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<UserRow?>(sql, new { Email = email });
        return row != null ? MapToData(row) : null;
    }

    public async Task<IReadOnlyList<UserData>> GetByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT Id, OrganizationId, Email, DisplayName, Phone, Role, CreatedAt, UpdatedAt FROM dbo.Users WHERE OrganizationId = @OrganizationId ORDER BY CreatedAt DESC";

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<UserRow>(sql, new { OrganizationId = organizationId });
        return rows.Select(MapToData).ToList();
    }

    public async Task<int> GetCountByOrganizationIdAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT COUNT(*) FROM dbo.Users WHERE OrganizationId = @OrganizationId";

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleAsync<int>(sql, new { OrganizationId = organizationId });
    }

    public async Task<Guid> CreateAsync(UserData user, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO dbo.Users (Id, OrganizationId, Email, DisplayName, Phone, Role, CreatedAt, UpdatedAt)
            VALUES (@Id, @OrganizationId, @Email, @DisplayName, @Phone, @Role, @CreatedAt, @UpdatedAt)";

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var row = new UserRow
        {
            Id = user.Id,
            OrganizationId = user.OrganizationId,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Phone = user.Phone,
            Role = user.Role,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
        await connection.ExecuteAsync(sql, row);
        return user.Id;
    }

    public async Task UpdateAsync(UserData user, CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE dbo.Users
            SET DisplayName = @DisplayName, Phone = @Phone, Role = @Role, UpdatedAt = @UpdatedAt
            WHERE Id = @Id";

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var row = new UserRow
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

    private static UserData MapToData(UserRow row) =>
        new()
        {
            Id = row.Id,
            OrganizationId = row.OrganizationId,
            Email = row.Email,
            DisplayName = row.DisplayName,
            Phone = row.Phone,
            Role = row.Role,
            CreatedAt = row.CreatedAt,
            UpdatedAt = row.UpdatedAt
        };
}
