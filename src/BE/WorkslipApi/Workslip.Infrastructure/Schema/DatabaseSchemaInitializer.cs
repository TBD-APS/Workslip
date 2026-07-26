using Microsoft.EntityFrameworkCore;

namespace Workslip.Infrastructure.Schema;

public sealed class DatabaseSchemaInitializer(SqlDbContext db)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.EnsureCreatedAsync(cancellationToken);
        await db.Database.MigrateAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("""
            IF OBJECT_ID(N'dbo.IdempotencyRecords', N'U') IS NULL
            BEGIN
                CREATE TABLE [dbo].[IdempotencyRecords] (
                    [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_IdempotencyRecords] PRIMARY KEY,
                    [Scope] nvarchar(200) NOT NULL,
                    [Key] nvarchar(128) NOT NULL,
                    [RequestHash] nvarchar(64) NOT NULL,
                    [ReservationToken] nvarchar(64) NOT NULL,
                    [Completed] bit NOT NULL DEFAULT (0),
                    [StatusCode] int NOT NULL DEFAULT (200),
                    [ResponseJson] nvarchar(max) NULL,
                    [CreatedAt] datetimeoffset NOT NULL,
                    [ExpiresAt] datetimeoffset NOT NULL
                );
                CREATE UNIQUE INDEX [UX_IdempotencyRecords_Scope_Key] ON [dbo].[IdempotencyRecords] ([Scope], [Key]);
                CREATE INDEX [IX_IdempotencyRecords_ExpiresAt] ON [dbo].[IdempotencyRecords] ([ExpiresAt]);
            END
            IF COL_LENGTH(N'dbo.IdempotencyRecords', N'ReservationToken') IS NULL
            BEGIN
                ALTER TABLE [dbo].[IdempotencyRecords] ADD [ReservationToken] nvarchar(64) NULL;
                UPDATE [dbo].[IdempotencyRecords] SET [ReservationToken] = CONVERT(nvarchar(64), NEWID()) WHERE [ReservationToken] IS NULL;
                ALTER TABLE [dbo].[IdempotencyRecords] ALTER COLUMN [ReservationToken] nvarchar(64) NOT NULL;
            END
            IF COL_LENGTH(N'dbo.NotificationQueue', N'ReadUtc') IS NULL
                ALTER TABLE [dbo].[NotificationQueue] ADD [ReadUtc] datetimeoffset NULL;

            IF COL_LENGTH(N'dbo.Customers', N'CustomerNumber') IS NULL
                ALTER TABLE [dbo].[Customers] ADD [CustomerNumber] nvarchar(80) NULL;
            ELSE IF COL_LENGTH(N'dbo.Customers', N'CustomerNumber') = -1
                ALTER TABLE [dbo].[Customers] ALTER COLUMN [CustomerNumber] nvarchar(80) NULL;
            IF COL_LENGTH(N'dbo.Customers', N'ZipCode') IS NULL
                ALTER TABLE [dbo].[Customers] ADD [ZipCode] nvarchar(20) NULL;
            ELSE IF COL_LENGTH(N'dbo.Customers', N'ZipCode') = -1
                ALTER TABLE [dbo].[Customers] ALTER COLUMN [ZipCode] nvarchar(20) NULL;
            IF COL_LENGTH(N'dbo.Customers', N'City') IS NULL
                ALTER TABLE [dbo].[Customers] ADD [City] nvarchar(120) NULL;
            ELSE IF COL_LENGTH(N'dbo.Customers', N'City') = -1
                ALTER TABLE [dbo].[Customers] ALTER COLUMN [City] nvarchar(120) NULL;
            IF COL_LENGTH(N'dbo.Customers', N'Country') IS NULL
                ALTER TABLE [dbo].[Customers] ADD [Country] nvarchar(120) NULL;
            ELSE IF COL_LENGTH(N'dbo.Customers', N'Country') = -1
                ALTER TABLE [dbo].[Customers] ALTER COLUMN [Country] nvarchar(120) NULL;

            IF COL_LENGTH(N'dbo.Customers', N'IsTop') IS NOT NULL AND COL_LENGTH(N'dbo.Customers', N'IsFavorite') IS NULL
                EXEC sp_rename N'dbo.Customers.IsTop', N'IsFavorite', N'COLUMN';

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE [name] = N'UX_Customers_Organization_CustomerNumber'
                  AND [object_id] = OBJECT_ID(N'dbo.Customers'))
            BEGIN
                CREATE UNIQUE INDEX [UX_Customers_Organization_CustomerNumber]
                    ON [dbo].[Customers] ([OrganizationId], [CustomerNumber])
                    WHERE [CustomerNumber] IS NOT NULL;
            END
            """, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(DatabaseIntegrityConstraintsSql.Apply, cancellationToken);
    }
}
