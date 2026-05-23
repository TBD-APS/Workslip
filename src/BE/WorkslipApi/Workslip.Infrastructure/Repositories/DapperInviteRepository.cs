using Dapper;
using Workslip.Application.Users;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Repositories;

public sealed class DapperInviteRepository(ISqlConnectionFactory connectionFactory) : IInviteRepository
{
    public async Task CreateAsync(InviteTokenRow invite, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.InviteTokens (Id, OrganizationId, Email, Token, Role, ExpiresAt, Consumed, CreatedAt)
            VALUES (@Id, @OrganizationId, @Email, @Token, @Role, @ExpiresAt, @Consumed, @CreatedAt)
            """;

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, invite);
    }

    public async Task<InviteTokenRow?> GetByTokenAsync(string token, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, OrganizationId, Email, Token, Role, ExpiresAt, Consumed, CreatedAt
            FROM dbo.InviteTokens
            WHERE Token = @Token
            """;

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<InviteTokenRow>(sql, new { Token = token });
    }

    public async Task<IReadOnlyList<InviteTokenRow>> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT Id, OrganizationId, Email, Token, Role, ExpiresAt, Consumed, CreatedAt
            FROM dbo.InviteTokens
            WHERE Email = @Email AND Consumed = 0 AND ExpiresAt > sysutcdatetime()
            ORDER BY CreatedAt DESC
            """;

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<InviteTokenRow>(sql, new { Email = email });
        return rows.ToList();
    }

    public async Task MarkConsumedAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE dbo.InviteTokens SET Consumed = 1 WHERE Id = @Id";

        using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(sql, new { Id = id });
    }
}
