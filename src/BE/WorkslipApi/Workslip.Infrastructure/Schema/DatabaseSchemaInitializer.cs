using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Workslip.Infrastructure.Schema;

public sealed class DatabaseSchemaInitializer(SqlDbContext db, IHostEnvironment environment)
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

            IF COL_LENGTH(N'dbo.InviteTokens', N'RevokedAt') IS NULL
                ALTER TABLE [dbo].[InviteTokens] ADD [RevokedAt] datetimeoffset NULL;

            IF EXISTS (
                SELECT 1
                FROM sys.columns
                WHERE [object_id] = OBJECT_ID(N'dbo.Users')
                  AND [name] = N'OrganizationId'
                  AND [is_nullable] = 0)
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM [dbo].[Worksheets] AS worksheet
                    INNER JOIN [dbo].[Users] AS appUser ON appUser.[Id] = worksheet.[UserId]
                    WHERE appUser.[Role] = N'Superadmin')
                    THROW 51000, 'Cannot make Superadmins platform-scoped while worksheet rows reference a Superadmin.', 1;

                IF EXISTS (
                    SELECT 1
                    FROM [dbo].[JobAssignments] AS assignment
                    INNER JOIN [dbo].[Users] AS appUser ON appUser.[Id] = assignment.[UserId]
                    WHERE appUser.[Role] = N'Superadmin')
                    THROW 51000, 'Cannot make Superadmins platform-scoped while job assignments reference a Superadmin.', 1;

                IF OBJECT_ID(N'dbo.FK_Worksheets_Users_OrganizationId_UserId', N'F') IS NOT NULL
                    ALTER TABLE [dbo].[Worksheets] DROP CONSTRAINT [FK_Worksheets_Users_OrganizationId_UserId];

                IF OBJECT_ID(N'dbo.FK_Users_Organizations_OrganizationId', N'F') IS NOT NULL
                    ALTER TABLE [dbo].[Users] DROP CONSTRAINT [FK_Users_Organizations_OrganizationId];

                IF EXISTS (
                    SELECT 1 FROM sys.key_constraints
                    WHERE [parent_object_id] = OBJECT_ID(N'dbo.Users')
                      AND [name] = N'AK_Users_OrganizationId_Id')
                    ALTER TABLE [dbo].[Users] DROP CONSTRAINT [AK_Users_OrganizationId_Id];

                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE [object_id] = OBJECT_ID(N'dbo.Users')
                      AND [name] = N'UX_Users_Organization_Id')
                    DROP INDEX [UX_Users_Organization_Id] ON [dbo].[Users];

                IF EXISTS (
                    SELECT 1 FROM sys.indexes
                    WHERE [object_id] = OBJECT_ID(N'dbo.Users')
                      AND [name] = N'IX_Users_OrganizationId')
                    DROP INDEX [IX_Users_OrganizationId] ON [dbo].[Users];

                ALTER TABLE [dbo].[Users] ALTER COLUMN [OrganizationId] uniqueidentifier NULL;
            END

            UPDATE [dbo].[Users]
            SET [OrganizationId] = NULL,
                [UpdatedAt] = SYSUTCDATETIME()
            WHERE [Role] = N'Superadmin'
              AND [OrganizationId] IS NOT NULL;

            IF EXISTS (
                SELECT 1
                FROM [dbo].[Users]
                WHERE ([Role] = N'Superadmin' AND [OrganizationId] IS NOT NULL)
                   OR ([Role] <> N'Superadmin' AND [OrganizationId] IS NULL))
                THROW 51000, 'Users contain invalid organization scope for their role.', 1;

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'dbo.Users')
                  AND [name] = N'IX_Users_OrganizationId')
                CREATE INDEX [IX_Users_OrganizationId] ON [dbo].[Users] ([OrganizationId]);

            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE [object_id] = OBJECT_ID(N'dbo.Users')
                  AND [name] = N'UX_Users_Organization_Id')
                CREATE UNIQUE INDEX [UX_Users_Organization_Id] ON [dbo].[Users] ([OrganizationId], [Id]);

            IF OBJECT_ID(N'dbo.FK_Users_Organizations_OrganizationId', N'F') IS NULL
            BEGIN
                ALTER TABLE [dbo].[Users] WITH CHECK
                    ADD CONSTRAINT [FK_Users_Organizations_OrganizationId]
                    FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Organizations] ([Id])
                    ON DELETE NO ACTION;
                ALTER TABLE [dbo].[Users] CHECK CONSTRAINT [FK_Users_Organizations_OrganizationId];
            END

            IF NOT EXISTS (
                SELECT 1
                FROM sys.indexes
                WHERE [name] = N'UX_Users_Email'
                  AND [object_id] = OBJECT_ID(N'dbo.Users'))
            BEGIN
                IF EXISTS (
                    SELECT [Email]
                    FROM [dbo].[Users]
                    WHERE [Email] IS NOT NULL AND LTRIM(RTRIM([Email])) <> N''
                    GROUP BY [Email]
                    HAVING COUNT_BIG(*) > 1)
                BEGIN
                    THROW 51000, 'Cannot create UX_Users_Email because duplicate non-empty user emails exist.', 1;
                END

                CREATE UNIQUE INDEX [UX_Users_Email]
                    ON [dbo].[Users] ([Email])
                    WHERE [Email] IS NOT NULL AND [Email] <> N'';
            END

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

        if (!environment.IsDevelopment())
        {
            await EnsureUserRoleOrganizationScopeConstraintAsync(db, cancellationToken);
        }

        await db.Database.ExecuteSqlRawAsync(DatabaseIntegrityConstraintsSql.Apply, cancellationToken);
    }

    public static Task EnsureUserRoleOrganizationScopeConstraintAsync(
        SqlDbContext context,
        CancellationToken cancellationToken = default) =>
        context.Database.ExecuteSqlRawAsync("""
            IF EXISTS (
                SELECT 1
                FROM [dbo].[Users]
                WHERE ([Role] = N'Superadmin' AND [OrganizationId] IS NOT NULL)
                   OR ([Role] <> N'Superadmin' AND [OrganizationId] IS NULL))
                THROW 51000, 'Users contain invalid organization scope for their role.', 1;

            IF OBJECT_ID(N'dbo.CK_Users_RoleOrganizationScope', N'C') IS NULL
            BEGIN
                ALTER TABLE [dbo].[Users] WITH CHECK
                    ADD CONSTRAINT [CK_Users_RoleOrganizationScope]
                    CHECK (([Role] = N'Superadmin' AND [OrganizationId] IS NULL)
                        OR ([Role] <> N'Superadmin' AND [OrganizationId] IS NOT NULL));
                ALTER TABLE [dbo].[Users] CHECK CONSTRAINT [CK_Users_RoleOrganizationScope];
            END
            """, cancellationToken);
}
