using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Workslip.Application.Invitations;
using Workslip.Domain.Models;
using Workslip.Infrastructure.Repositories;
using Workslip.Infrastructure.Resilience;
using Workslip.Infrastructure.Schema;
using Xunit;

namespace Workslip.Tests.Invitations;

public sealed class EfInviteRepositoryTests
{
    [Fact]
    public async Task GetInviteByEmailAsync_WhenTenantsShareEmail_ReturnsOnlyRequestedTenantInvite()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var firstOrganizationId = Guid.NewGuid();
        var secondOrganizationId = Guid.NewGuid();
        var email = "shared@example.test";
        var firstInvite = CreateInvite(firstOrganizationId, email);
        var secondInvite = CreateInvite(secondOrganizationId, email);

        database.Context.Organizations.AddRange(
            CreateOrganization(firstOrganizationId, "12345678"),
            CreateOrganization(secondOrganizationId, "87654321"));
        database.Context.InviteTokens.AddRange(firstInvite, secondInvite);
        await database.Context.SaveChangesAsync();

        var repository = new EfInviteRepository(database.Context, new NoRetryPolicy());

        var result = await repository.GetInviteByEmailAsync(
            secondOrganizationId,
            email,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(secondInvite.Id, result.Id);
        Assert.Equal(secondOrganizationId, result.OrganizationId);
    }

    [Fact]
    public async Task MarkConsumedAsync_WhenRevocationAlreadyWon_ThrowsAndLeavesInviteRevoked()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var organizationId = Guid.NewGuid();
        var invite = CreateInvite(organizationId, "race@example.test");
        database.Context.Organizations.Add(CreateOrganization(organizationId, "12345678"));
        database.Context.InviteTokens.Add(invite);
        await database.Context.SaveChangesAsync();

        var repository = new EfInviteRepository(database.Context, new NoRetryPolicy());
        var revokedAt = DateTimeOffset.UtcNow;
        var replacementToken = Guid.NewGuid().ToString("N");

        var revoked = await repository.TryRevokePendingAsync(
            organizationId,
            invite.Id,
            invite.Token,
            revokedAt,
            replacementToken,
            CancellationToken.None);

        Assert.True(revoked);
        await Assert.ThrowsAsync<InviteStateChangedException>(
            () => repository.MarkConsumedAsync(invite, CancellationToken.None));

        var stored = await database.Context.InviteTokens
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == invite.Id);
        Assert.False(stored.Consumed);
        Assert.Equal(revokedAt, stored.RevokedAt);
        Assert.Equal(replacementToken, stored.Token);
    }

    [Fact]
    public async Task GetStaleEntraProvisionedAsync_RevokesInviteBeforeReturningItForCleanup()
    {
        await using var database = await RelationalTestDatabase.CreateAsync();
        var organizationId = Guid.NewGuid();
        var invite = CreateInvite(organizationId, "expired@example.test");
        invite.ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1);
        invite.EntraUserId = "entra-user";
        invite.EntraCreatedByInvite = true;
        database.Context.Organizations.Add(CreateOrganization(organizationId, "12345678"));
        database.Context.InviteTokens.Add(invite);
        await database.Context.SaveChangesAsync();

        var repository = new EfInviteRepository(database.Context, new NoRetryPolicy());
        var originalToken = invite.Token;
        var cleanupStartedAt = DateTimeOffset.UtcNow;

        var staleInvites = await repository.GetStaleEntraProvisionedAsync(
            cleanupStartedAt,
            10,
            CancellationToken.None);

        var claimed = Assert.Single(staleInvites);
        Assert.NotNull(claimed.RevokedAt);
        Assert.NotEqual(originalToken, claimed.Token);

        var stored = await database.Context.InviteTokens
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == invite.Id);
        Assert.False(stored.Consumed);
        Assert.NotNull(stored.RevokedAt);
        Assert.Equal(claimed.Token, stored.Token);
    }

    private static InviteTokenRow CreateInvite(Guid organizationId, string email) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        Email = email,
        Token = Guid.NewGuid().ToString("N"),
        Role = "User",
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static OrganizationRow CreateOrganization(Guid id, string cvr) => new()
    {
        Id = id,
        Name = $"Organization {id:N}",
        Cvr = cvr,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private sealed class NoRetryPolicy : IDatabaseRetryPolicy
    {
        public Task ExecuteAsync(
            string operationName,
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken) =>
            operation(cancellationToken);

        public Task<T> ExecuteAsync<T>(
            string operationName,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken) =>
            operation(cancellationToken);
    }

    private sealed class RelationalTestDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection connection;

        private RelationalTestDatabase(SqliteConnection connection, SqlDbContext context)
        {
            this.connection = connection;
            Context = context;
        }

        internal SqlDbContext Context { get; }

        internal static async Task<RelationalTestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            connection.CreateFunction(
                "sysutcdatetime",
                () => DateTimeOffset.UtcNow.ToString("O"),
                isDeterministic: false);
            await connection.OpenAsync();

            var options = new DbContextOptionsBuilder<SqlDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new SqlDbContext(options);
            await context.Database.EnsureCreatedAsync();
            await context.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON;");

            return new RelationalTestDatabase(connection, context);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
