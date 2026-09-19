using System.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Workslip.Application.Integrations;
using Workslip.Infrastructure.Schema;

namespace Workslip.Infrastructure.Repositories;

public sealed class SqlAccountingSyncRepository(SqlDbContext dbContext) : IAccountingSyncRepository
{
    public async Task<IReadOnlyList<AccountingLocalCustomer>> ListCustomersAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        return await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                $"""
                SELECT Id, CustomerNumber, Name, Address, ZipCode, City, Country, Email, ContactPerson, Phone
                FROM {Table("Customers")}
                WHERE OrganizationId = @OrganizationId
                ORDER BY Name, Id;
                """,
                new { OrganizationId = organizationId }, transaction, cancellationToken: cancellationToken);
            return (await connection.QueryAsync<AccountingLocalCustomer>(command)).AsList();
        }, cancellationToken);
    }

    public async Task<AccountingLocalCustomer?> GetCustomerAsync(Guid organizationId, Guid customerId, CancellationToken cancellationToken)
    {
        return await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                $"""
                SELECT Id, CustomerNumber, Name, Address, ZipCode, City, Country, Email, ContactPerson, Phone
                FROM {Table("Customers")}
                WHERE OrganizationId = @OrganizationId AND Id = @CustomerId;
                """,
                new { OrganizationId = organizationId, CustomerId = customerId }, transaction, cancellationToken: cancellationToken);
            return await connection.QuerySingleOrDefaultAsync<AccountingLocalCustomer>(command);
        }, cancellationToken);
    }

    public async Task<Guid> UpsertLocalCustomerAsync(Guid organizationId, ExternalAccountingCustomer customer, CancellationToken cancellationToken)
    {
        return await WithConnectionAsync(async (connection, transaction) =>
        {
            var existingId = await connection.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                $"SELECT Id FROM {Table("Customers")} WHERE OrganizationId = @OrganizationId AND CustomerNumber = @CustomerNumber",
                new { OrganizationId = organizationId, CustomerNumber = customer.ExternalCustomerNumber }, transaction, cancellationToken: cancellationToken));

            var id = existingId ?? Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            if (existingId is null)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    $"""
                    INSERT INTO {Table("Customers")}
                        (Id, OrganizationId, CustomerNumber, Name, Address, ZipCode, City, Country, Email, ContactPerson, Phone, IsFavorite, CreatedAt, UpdatedAt)
                    VALUES
                        (@Id, @OrganizationId, @CustomerNumber, @Name, @Address, @ZipCode, @City, @Country, @Email, @ContactPerson, @Phone, 0, @Now, @Now);
                    """,
                    new
                    {
                        Id = id,
                        OrganizationId = organizationId,
                        CustomerNumber = customer.ExternalCustomerNumber,
                        customer.Name,
                        customer.Address,
                        customer.ZipCode,
                        customer.City,
                        customer.Country,
                        customer.Email,
                        customer.ContactPerson,
                        customer.Phone,
                        Now = now
                    }, transaction, cancellationToken: cancellationToken));
            }
            else
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    $"""
                    UPDATE {Table("Customers")}
                    SET Name = @Name,
                        Address = @Address,
                        ZipCode = @ZipCode,
                        City = @City,
                        Country = @Country,
                        Email = @Email,
                        ContactPerson = COALESCE(@ContactPerson, ContactPerson),
                        Phone = @Phone,
                        UpdatedAt = @Now
                    WHERE OrganizationId = @OrganizationId AND Id = @Id;
                    """,
                    new
                    {
                        Id = id,
                        OrganizationId = organizationId,
                        customer.Name,
                        customer.Address,
                        customer.ZipCode,
                        customer.City,
                        customer.Country,
                        customer.Email,
                        customer.ContactPerson,
                        customer.Phone,
                        Now = now
                    }, transaction, cancellationToken: cancellationToken));
            }

            return id;
        }, cancellationToken);
    }

    public async Task<AccountingCustomerLink?> GetCustomerLinkAsync(Guid organizationId, Guid customerId, string providerId, CancellationToken cancellationToken)
    {
        return await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                $"""
                SELECT ExternalCustomerNumber, LastSyncedAt
                FROM {Table("AccountingCustomerLinks")}
                WHERE OrganizationId = @OrganizationId AND CustomerId = @CustomerId AND ProviderId = @ProviderId;
                """,
                new { OrganizationId = organizationId, CustomerId = customerId, ProviderId = providerId }, transaction, cancellationToken: cancellationToken);
            return await connection.QuerySingleOrDefaultAsync<AccountingCustomerLink>(command);
        }, cancellationToken);
    }

    public async Task UpsertCustomerLinkAsync(Guid organizationId, Guid customerId, string providerId, string externalCustomerNumber, CancellationToken cancellationToken)
    {
        await WithConnectionAsync(async (connection, transaction) =>
        {
            var now = DateTimeOffset.UtcNow;
            var sql = IsSqlServer
                ? $"""
                   UPDATE {Table("AccountingCustomerLinks")}
                   SET ExternalCustomerNumber = @ExternalCustomerNumber, LastSyncedAt = @Now
                   WHERE OrganizationId = @OrganizationId AND CustomerId = @CustomerId AND ProviderId = @ProviderId;
                   IF @@ROWCOUNT = 0
                       INSERT INTO {Table("AccountingCustomerLinks")}
                           (OrganizationId, CustomerId, ProviderId, ExternalCustomerNumber, LastSyncedAt)
                       VALUES (@OrganizationId, @CustomerId, @ProviderId, @ExternalCustomerNumber, @Now);
                   """
                : $"""
                   INSERT INTO {Table("AccountingCustomerLinks")}
                       (OrganizationId, CustomerId, ProviderId, ExternalCustomerNumber, LastSyncedAt)
                   VALUES (@OrganizationId, @CustomerId, @ProviderId, @ExternalCustomerNumber, @Now)
                   ON CONFLICT(OrganizationId, CustomerId, ProviderId) DO UPDATE SET
                       ExternalCustomerNumber = excluded.ExternalCustomerNumber,
                       LastSyncedAt = excluded.LastSyncedAt;
                   """;
            await connection.ExecuteAsync(new CommandDefinition(sql,
                new { OrganizationId = organizationId, CustomerId = customerId, ProviderId = providerId, ExternalCustomerNumber = externalCustomerNumber, Now = now },
                transaction, cancellationToken: cancellationToken));
            return 0;
        }, cancellationToken);
    }

    public async Task<AccountingInvoiceSource?> GetInvoiceSourceAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken)
    {
        return await WithConnectionAsync(async (connection, transaction) =>
        {
            var header = await connection.QuerySingleOrDefaultAsync<InvoiceHeaderRow>(new CommandDefinition(
                $"""
                SELECT j.Id AS JobId, j.Status, j.ReportNumber,
                       c.Id AS CustomerId, c.CustomerNumber, c.Name, c.Address, c.ZipCode, c.City, c.Country, c.Email, c.ContactPerson, c.Phone
                FROM {Table("JobReports")} j
                INNER JOIN {Table("Customers")} c ON c.Id = j.CustomerId AND c.OrganizationId = j.OrganizationId
                WHERE j.OrganizationId = @OrganizationId AND j.Id = @JobId AND j.IsSoftDeleted = 0;
                """,
                new { OrganizationId = organizationId, JobId = jobId }, transaction, cancellationToken: cancellationToken));
            if (header is null) return null;

            var timeRows = (await connection.QueryAsync<InvoiceTimeRow>(new CommandDefinition(
                $"""
                SELECT w.WorkDate, w.HoursWorked, COALESCE(NULLIF(u.DisplayName, ''), u.Email, 'Medarbejder') AS EmployeeName,
                       COALESCE(s.BillableHourlyRateSnapshot, r.BillableHourlyRate) AS BillableRate
                FROM {Table("Worksheets")} w
                LEFT JOIN {Table("Users")} u ON u.Id = w.UserId AND u.OrganizationId = w.OrganizationId
                LEFT JOIN {Table("WorksheetBillingSnapshots")} s ON s.OrganizationId = w.OrganizationId AND s.WorksheetId = w.Id
                LEFT JOIN {Table("UserBillingRates")} r ON r.OrganizationId = w.OrganizationId AND r.UserId = w.UserId
                WHERE w.OrganizationId = @OrganizationId AND w.JobId = @JobId
                ORDER BY w.WorkDate, EmployeeName;
                """,
                new { OrganizationId = organizationId, JobId = jobId }, transaction, cancellationToken: cancellationToken))).AsList();

            var lines = timeRows
                .Where(row => row.BillableRate is not null && row.HoursWorked > 0)
                .GroupBy(row => new { row.EmployeeName, Rate = row.BillableRate!.Value })
                .Select(group => new AccountingDraftInvoiceLine(
                    "hours",
                    $"Timer – {group.Key.EmployeeName}",
                    group.Sum(row => row.HoursWorked),
                    group.Key.Rate))
                .ToList();

            var missingRateHours = timeRows
                .Where(row => row.BillableRate is null)
                .Sum(row => row.HoursWorked);

            var billableItems = (await connection.QueryAsync<JobBillableItemResponse>(new CommandDefinition(
                $"""
                SELECT Id, JobId, Kind, Description, Quantity, UnitNetPrice,
                       Quantity * UnitNetPrice AS LineNetAmount, Source
                FROM {Table("JobBillableItems")}
                WHERE OrganizationId = @OrganizationId AND JobId = @JobId
                ORDER BY CreatedAt, Id;
                """,
                new { OrganizationId = organizationId, JobId = jobId }, transaction, cancellationToken: cancellationToken))).AsList();

            lines.AddRange(billableItems.Select(item => new AccountingDraftInvoiceLine(
                item.Kind, item.Description, item.Quantity, item.UnitNetPrice)));

            var customer = new AccountingLocalCustomer(
                header.CustomerId,
                header.CustomerNumber,
                header.Name,
                header.Address,
                header.ZipCode,
                header.City,
                header.Country,
                header.Email,
                header.ContactPerson,
                header.Phone);

            return new AccountingInvoiceSource(
                header.JobId,
                header.Status,
                header.ReportNumber,
                header.CustomerId,
                customer,
                lines,
                missingRateHours);
        }, cancellationToken);
    }

    public async Task<JobAccountingInvoiceResponse?> GetJobInvoiceLinkAsync(Guid organizationId, Guid jobId, string providerId, CancellationToken cancellationToken)
    {
        return await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                $"""
                SELECT JobId, ProviderId, DraftInvoiceNumber, BookedInvoiceNumber, Status, ExternalReference, ExternalUrl, NetAmount, LastSyncedAt
                FROM {Table("JobAccountingLinks")}
                WHERE OrganizationId = @OrganizationId AND JobId = @JobId AND ProviderId = @ProviderId;
                """,
                new { OrganizationId = organizationId, JobId = jobId, ProviderId = providerId }, transaction, cancellationToken: cancellationToken);
            return await connection.QuerySingleOrDefaultAsync<JobAccountingInvoiceResponse>(command);
        }, cancellationToken);
    }

    public async Task UpsertJobInvoiceLinkAsync(Guid organizationId, Guid jobId, string providerId, AccountingInvoiceState state, CancellationToken cancellationToken)
    {
        await WithConnectionAsync(async (connection, transaction) =>
        {
            var now = DateTimeOffset.UtcNow;
            var sql = IsSqlServer
                ? $"""
                   UPDATE {Table("JobAccountingLinks")}
                   SET DraftInvoiceNumber = @DraftInvoiceNumber,
                       BookedInvoiceNumber = @BookedInvoiceNumber,
                       ExternalReference = @ExternalReference,
                       Status = @Status,
                       ExternalUrl = @ExternalUrl,
                       NetAmount = @NetAmount,
                       Remainder = @Remainder,
                       DueDate = @DueDate,
                       LastSyncedAt = @Now
                   WHERE OrganizationId = @OrganizationId AND JobId = @JobId AND ProviderId = @ProviderId;
                   IF @@ROWCOUNT = 0
                       INSERT INTO {Table("JobAccountingLinks")}
                           (OrganizationId, JobId, ProviderId, DraftInvoiceNumber, BookedInvoiceNumber, ExternalReference, Status, ExternalUrl, NetAmount, Remainder, DueDate, CreatedAt, LastSyncedAt)
                       VALUES
                           (@OrganizationId, @JobId, @ProviderId, @DraftInvoiceNumber, @BookedInvoiceNumber, @ExternalReference, @Status, @ExternalUrl, @NetAmount, @Remainder, @DueDate, @Now, @Now);
                   """
                : $"""
                   INSERT INTO {Table("JobAccountingLinks")}
                       (OrganizationId, JobId, ProviderId, DraftInvoiceNumber, BookedInvoiceNumber, ExternalReference, Status, ExternalUrl, NetAmount, Remainder, DueDate, CreatedAt, LastSyncedAt)
                   VALUES
                       (@OrganizationId, @JobId, @ProviderId, @DraftInvoiceNumber, @BookedInvoiceNumber, @ExternalReference, @Status, @ExternalUrl, @NetAmount, @Remainder, @DueDate, @Now, @Now)
                   ON CONFLICT(OrganizationId, JobId, ProviderId) DO UPDATE SET
                       DraftInvoiceNumber = excluded.DraftInvoiceNumber,
                       BookedInvoiceNumber = excluded.BookedInvoiceNumber,
                       ExternalReference = excluded.ExternalReference,
                       Status = excluded.Status,
                       ExternalUrl = excluded.ExternalUrl,
                       NetAmount = excluded.NetAmount,
                       Remainder = excluded.Remainder,
                       DueDate = excluded.DueDate,
                       LastSyncedAt = excluded.LastSyncedAt;
                   """;
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                OrganizationId = organizationId,
                JobId = jobId,
                ProviderId = providerId,
                state.DraftInvoiceNumber,
                state.BookedInvoiceNumber,
                state.ExternalReference,
                state.Status,
                state.ExternalUrl,
                state.NetAmount,
                state.Remainder,
                state.DueDate,
                Now = now
            }, transaction, cancellationToken: cancellationToken));
            return 0;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<JobBillableItemResponse>> ListBillableItemsAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken)
    {
        return await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                $"""
                SELECT Id, JobId, Kind, Description, Quantity, UnitNetPrice,
                       Quantity * UnitNetPrice AS LineNetAmount, Source
                FROM {Table("JobBillableItems")}
                WHERE OrganizationId = @OrganizationId AND JobId = @JobId
                ORDER BY CreatedAt, Id;
                """,
                new { OrganizationId = organizationId, JobId = jobId }, transaction, cancellationToken: cancellationToken);
            return (await connection.QueryAsync<JobBillableItemResponse>(command)).AsList();
        }, cancellationToken);
    }

    public async Task<JobBillableItemResponse> UpsertBillableItemAsync(Guid organizationId, Guid jobId, Guid? itemId, UpsertJobBillableItemRequest request, CancellationToken cancellationToken)
    {
        return await WithConnectionAsync(async (connection, transaction) =>
        {
            await EnsureJobAsync(connection, transaction, organizationId, jobId, cancellationToken);
            var id = itemId ?? Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            var source = string.IsNullOrWhiteSpace(request.Source) ? "manual" : request.Source!.Trim();

            if (itemId is null)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    $"""
                    INSERT INTO {Table("JobBillableItems")}
                        (Id, OrganizationId, JobId, Kind, Description, Quantity, UnitNetPrice, Source, CreatedAt, UpdatedAt)
                    VALUES (@Id, @OrganizationId, @JobId, @Kind, @Description, @Quantity, @UnitNetPrice, @Source, @Now, @Now);
                    """,
                    new { Id = id, OrganizationId = organizationId, JobId = jobId, request.Kind, Description = request.Description.Trim(), request.Quantity, request.UnitNetPrice, Source = source, Now = now },
                    transaction, cancellationToken: cancellationToken));
            }
            else
            {
                var affected = await connection.ExecuteAsync(new CommandDefinition(
                    $"""
                    UPDATE {Table("JobBillableItems")}
                    SET Kind = @Kind, Description = @Description, Quantity = @Quantity, UnitNetPrice = @UnitNetPrice, Source = @Source, UpdatedAt = @Now
                    WHERE Id = @Id AND OrganizationId = @OrganizationId AND JobId = @JobId;
                    """,
                    new { Id = id, OrganizationId = organizationId, JobId = jobId, request.Kind, Description = request.Description.Trim(), request.Quantity, request.UnitNetPrice, Source = source, Now = now },
                    transaction, cancellationToken: cancellationToken));
                if (affected == 0) throw new KeyNotFoundException("Billable item not found.");
            }

            var result = await connection.QuerySingleAsync<JobBillableItemResponse>(new CommandDefinition(
                $"""
                SELECT Id, JobId, Kind, Description, Quantity, UnitNetPrice,
                       Quantity * UnitNetPrice AS LineNetAmount, Source
                FROM {Table("JobBillableItems")}
                WHERE Id = @Id AND OrganizationId = @OrganizationId AND JobId = @JobId;
                """,
                new { Id = id, OrganizationId = organizationId, JobId = jobId }, transaction, cancellationToken: cancellationToken));
            return result;
        }, cancellationToken);
    }

    public async Task DeleteBillableItemAsync(Guid organizationId, Guid jobId, Guid itemId, CancellationToken cancellationToken)
    {
        await WithConnectionAsync(async (connection, transaction) =>
        {
            await connection.ExecuteAsync(new CommandDefinition(
                $"DELETE FROM {Table("JobBillableItems")} WHERE Id = @ItemId AND OrganizationId = @OrganizationId AND JobId = @JobId",
                new { ItemId = itemId, OrganizationId = organizationId, JobId = jobId }, transaction, cancellationToken: cancellationToken));
            return 0;
        }, cancellationToken);
    }

    public async Task LinkDocumentAsync(Guid organizationId, Guid jobId, string providerId, LinkAccountingDocumentRequest request, CancellationToken cancellationToken)
    {
        await WithConnectionAsync(async (connection, transaction) =>
        {
            await EnsureJobAsync(connection, transaction, organizationId, jobId, cancellationToken);
            var sql = IsSqlServer
                ? $"""
                   UPDATE {Table("JobAccountingDocumentLinks")}
                   SET DocumentNumber = @DocumentNumber, DocumentType = @DocumentType, Amount = @Amount,
                       DocumentDate = @DocumentDate, Status = @Status, ExternalUrl = @ExternalUrl
                   WHERE OrganizationId = @OrganizationId AND JobId = @JobId AND ProviderId = @ProviderId AND ExternalDocumentId = @ExternalDocumentId;
                   IF @@ROWCOUNT = 0
                       INSERT INTO {Table("JobAccountingDocumentLinks")}
                           (OrganizationId, JobId, ProviderId, ExternalDocumentId, DocumentNumber, DocumentType, Amount, DocumentDate, Status, ExternalUrl, LinkedAt)
                       VALUES
                           (@OrganizationId, @JobId, @ProviderId, @ExternalDocumentId, @DocumentNumber, @DocumentType, @Amount, @DocumentDate, @Status, @ExternalUrl, @Now);
                   """
                : $"""
                   INSERT INTO {Table("JobAccountingDocumentLinks")}
                       (OrganizationId, JobId, ProviderId, ExternalDocumentId, DocumentNumber, DocumentType, Amount, DocumentDate, Status, ExternalUrl, LinkedAt)
                   VALUES
                       (@OrganizationId, @JobId, @ProviderId, @ExternalDocumentId, @DocumentNumber, @DocumentType, @Amount, @DocumentDate, @Status, @ExternalUrl, @Now)
                   ON CONFLICT(OrganizationId, JobId, ProviderId, ExternalDocumentId) DO UPDATE SET
                       DocumentNumber = excluded.DocumentNumber,
                       DocumentType = excluded.DocumentType,
                       Amount = excluded.Amount,
                       DocumentDate = excluded.DocumentDate,
                       Status = excluded.Status,
                       ExternalUrl = excluded.ExternalUrl;
                   """;
            await connection.ExecuteAsync(new CommandDefinition(sql, new
            {
                OrganizationId = organizationId,
                JobId = jobId,
                ProviderId = providerId,
                request.ExternalDocumentId,
                request.DocumentNumber,
                request.DocumentType,
                request.Amount,
                request.DocumentDate,
                request.Status,
                request.ExternalUrl,
                Now = DateTimeOffset.UtcNow
            }, transaction, cancellationToken: cancellationToken));
            return 0;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<JobAccountingDocumentResponse>> ListLinkedDocumentsAsync(Guid organizationId, Guid jobId, CancellationToken cancellationToken)
    {
        return await WithConnectionAsync(async (connection, transaction) =>
        {
            var command = new CommandDefinition(
                $"""
                SELECT JobId, ProviderId, ExternalDocumentId, DocumentNumber, DocumentType, Amount, DocumentDate, Status, ExternalUrl, LinkedAt
                FROM {Table("JobAccountingDocumentLinks")}
                WHERE OrganizationId = @OrganizationId AND JobId = @JobId
                ORDER BY DocumentDate DESC, LinkedAt DESC;
                """,
                new { OrganizationId = organizationId, JobId = jobId }, transaction, cancellationToken: cancellationToken);
            return (await connection.QueryAsync<JobAccountingDocumentResponse>(command)).AsList();
        }, cancellationToken);
    }

    private async Task EnsureJobAsync(IDbConnection connection, IDbTransaction? transaction, Guid organizationId, Guid jobId, CancellationToken cancellationToken)
    {
        var exists = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(1) FROM {Table("JobReports")} WHERE OrganizationId = @OrganizationId AND Id = @JobId AND IsSoftDeleted = 0",
            new { OrganizationId = organizationId, JobId = jobId }, transaction, cancellationToken: cancellationToken));
        if (exists == 0) throw new KeyNotFoundException("Job not found in the current organization.");
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

    private sealed record InvoiceHeaderRow(
        Guid JobId,
        string Status,
        string? ReportNumber,
        Guid CustomerId,
        string? CustomerNumber,
        string Name,
        string? Address,
        string? ZipCode,
        string? City,
        string? Country,
        string? Email,
        string? ContactPerson,
        string? Phone);

    private sealed record InvoiceTimeRow(DateOnly WorkDate, decimal HoursWorked, string EmployeeName, decimal? BillableRate);
}
