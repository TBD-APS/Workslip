using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Workslip.Application.Integrations;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class SqlEconomicConnectionStore(
    SqlDbContext dbContext,
    IConfiguration configuration) : IEconomicConnectionStore
{
    private const byte CipherVersion = 1;
    private const int NonceLength = 12;
    private const int TagLength = 16;

    public async Task<bool> HasConnectionAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await WithConnectionAsync(async (connection, transaction) =>
        {
            var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                $"SELECT COUNT(1) FROM {Table("EconomicConnections")} WHERE OrganizationId = @OrganizationId",
                new { OrganizationId = organizationId }, transaction, cancellationToken: cancellationToken));
            return count > 0;
        }, cancellationToken);

    public async Task<string?> GetAgreementGrantTokenAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await WithConnectionAsync(async (connection, transaction) =>
        {
            var ciphertext = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                $"SELECT AgreementGrantTokenCiphertext FROM {Table("EconomicConnections")} WHERE OrganizationId = @OrganizationId",
                new { OrganizationId = organizationId }, transaction, cancellationToken: cancellationToken));
            return string.IsNullOrWhiteSpace(ciphertext) ? null : Decrypt(organizationId, ciphertext);
        }, cancellationToken);

    public async Task<EconomicConnectionMetadata?> GetConnectionMetadataAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await WithConnectionAsync(async (connection, transaction) =>
            await connection.QuerySingleOrDefaultAsync<EconomicConnectionMetadata>(new CommandDefinition(
                $"""
                SELECT AgreementNumber, CompanyName, ConnectedAt, UpdatedAt
                FROM {Table("EconomicConnections")}
                WHERE OrganizationId = @OrganizationId;
                """,
                new { OrganizationId = organizationId }, transaction, cancellationToken: cancellationToken)),
            cancellationToken);

    public async Task SaveConnectionAsync(
        Guid organizationId,
        string agreementGrantToken,
        EconomicAgreementIdentity identity,
        CancellationToken cancellationToken)
    {
        var ciphertext = Encrypt(organizationId, agreementGrantToken);
        await WithConnectionAsync(async (connection, transaction) =>
        {
            var now = DateTimeOffset.UtcNow;
            var sql = IsSqlServer
                ? $"""
                   UPDATE {Table("EconomicConnections")}
                   SET AgreementGrantTokenCiphertext = @Ciphertext,
                       AgreementNumber = @AgreementNumber,
                       CompanyName = @CompanyName,
                       UpdatedAt = @Now
                   WHERE OrganizationId = @OrganizationId;
                   IF @@ROWCOUNT = 0
                       INSERT INTO {Table("EconomicConnections")}
                           (OrganizationId, AgreementGrantTokenCiphertext, AgreementNumber, CompanyName, ConnectedAt, UpdatedAt)
                       VALUES
                           (@OrganizationId, @Ciphertext, @AgreementNumber, @CompanyName, @Now, @Now);
                   """
                : $"""
                   INSERT INTO {Table("EconomicConnections")}
                       (OrganizationId, AgreementGrantTokenCiphertext, AgreementNumber, CompanyName, ConnectedAt, UpdatedAt)
                   VALUES
                       (@OrganizationId, @Ciphertext, @AgreementNumber, @CompanyName, @Now, @Now)
                   ON CONFLICT(OrganizationId) DO UPDATE SET
                       AgreementGrantTokenCiphertext = excluded.AgreementGrantTokenCiphertext,
                       AgreementNumber = excluded.AgreementNumber,
                       CompanyName = excluded.CompanyName,
                       UpdatedAt = excluded.UpdatedAt;
                   """;

            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                OrganizationId = organizationId,
                Ciphertext = ciphertext,
                identity.AgreementNumber,
                identity.CompanyName,
                Now = now
            }, transaction, cancellationToken: cancellationToken));
            return 0;
        }, cancellationToken);
    }

    public async Task DeleteConnectionAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        await WithConnectionAsync(async (connection, transaction) =>
        {
            await connection.ExecuteAsync(new CommandDefinition(
                $"DELETE FROM {Table("EconomicConnections")} WHERE OrganizationId = @OrganizationId",
                new { OrganizationId = organizationId }, transaction, cancellationToken: cancellationToken));
            return 0;
        }, cancellationToken);
    }

    public async Task SaveConnectAttemptAsync(
        Guid organizationId,
        string correlationHash,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        await WithConnectionAsync(async (connection, transaction) =>
        {
            var now = DateTimeOffset.UtcNow;
            await connection.ExecuteAsync(new CommandDefinition(
                $"DELETE FROM {Table("EconomicConnectAttempts")} WHERE OrganizationId = @OrganizationId OR ExpiresAt <= @Now",
                new { OrganizationId = organizationId, Now = now }, transaction, cancellationToken: cancellationToken));

            await connection.ExecuteAsync(new CommandDefinition(
                $"""
                INSERT INTO {Table("EconomicConnectAttempts")}
                    (CorrelationHash, OrganizationId, ExpiresAt, CreatedAt)
                VALUES
                    (@CorrelationHash, @OrganizationId, @ExpiresAt, @Now);
                """,
                new { CorrelationHash = correlationHash, OrganizationId = organizationId, ExpiresAt = expiresAt, Now = now },
                transaction, cancellationToken: cancellationToken));
            return 0;
        }, cancellationToken);
    }

    public async Task<Guid?> ConsumeConnectAttemptAsync(
        string correlationHash,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await WithConnectionAsync(async (connection, transaction) =>
        {
            if (IsSqlServer)
            {
                return await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                    $"""
                    DELETE FROM {Table("EconomicConnectAttempts")}
                    OUTPUT deleted.OrganizationId
                    WHERE CorrelationHash = @CorrelationHash AND ExpiresAt > @Now;
                    """,
                    new { CorrelationHash = correlationHash, Now = now }, transaction, cancellationToken: cancellationToken));
            }

            var organizationId = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                $"SELECT OrganizationId FROM {Table("EconomicConnectAttempts")} WHERE CorrelationHash = @CorrelationHash AND ExpiresAt > @Now",
                new { CorrelationHash = correlationHash, Now = now }, transaction, cancellationToken: cancellationToken));
            if (organizationId is not null)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    $"DELETE FROM {Table("EconomicConnectAttempts")} WHERE CorrelationHash = @CorrelationHash",
                    new { CorrelationHash = correlationHash }, transaction, cancellationToken: cancellationToken));
            }
            return organizationId;
        }, cancellationToken);

    private string Encrypt(Guid organizationId, string token)
    {
        var plaintext = Encoding.UTF8.GetBytes(token);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];
        using var aes = new AesGcm(GetEncryptionKey(), TagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, organizationId.ToByteArray());

        var payload = new byte[1 + NonceLength + TagLength + ciphertext.Length];
        payload[0] = CipherVersion;
        Buffer.BlockCopy(nonce, 0, payload, 1, NonceLength);
        Buffer.BlockCopy(tag, 0, payload, 1 + NonceLength, TagLength);
        Buffer.BlockCopy(ciphertext, 0, payload, 1 + NonceLength + TagLength, ciphertext.Length);
        CryptographicOperations.ZeroMemory(plaintext);
        return Convert.ToBase64String(payload);
    }

    private string Decrypt(Guid organizationId, string encoded)
    {
        var payload = Convert.FromBase64String(encoded);
        if (payload.Length <= 1 + NonceLength + TagLength || payload[0] != CipherVersion)
            throw new CryptographicException("Unsupported e-conomic token ciphertext.");

        var nonce = payload.AsSpan(1, NonceLength);
        var tag = payload.AsSpan(1 + NonceLength, TagLength);
        var ciphertext = payload.AsSpan(1 + NonceLength + TagLength);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(GetEncryptionKey(), TagLength);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, organizationId.ToByteArray());
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private byte[] GetEncryptionKey()
    {
        var material = configuration["Integrations:Economic:TokenEncryptionKey"]?.Trim();
        if (string.IsNullOrWhiteSpace(material) || material.Length < 32)
            throw new InvalidOperationException("e-conomic token encryption key must be configured with at least 32 characters.");

        return SHA256.HashData(Encoding.UTF8.GetBytes($"workslip:economic:grant-token:v1:{material}"));
    }

    private bool IsSqlServer => string.Equals(dbContext.Database.ProviderName, "Microsoft.EntityFrameworkCore.SqlServer", StringComparison.Ordinal);
    private string Table(string name) => IsSqlServer ? $"dbo.{name}" : name;

    private async Task<T> WithConnectionAsync<T>(
        Func<IDbConnection, IDbTransaction?, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose) await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            return await action(connection, transaction);
        }
        finally
        {
            if (shouldClose) await dbContext.Database.CloseConnectionAsync();
        }
    }
}
