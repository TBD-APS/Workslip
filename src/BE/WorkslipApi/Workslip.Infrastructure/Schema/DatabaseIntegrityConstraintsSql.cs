namespace Workslip.Infrastructure.Schema;

internal static class DatabaseIntegrityConstraintsSql
{
    internal const string Apply = """
        SET XACT_ABORT ON;

        BEGIN TRANSACTION;
        BEGIN TRY
            DECLARE @AppLockResult int;
            EXECUTE @AppLockResult = sys.sp_getapplock
                @Resource = N'Workslip.DatabaseIntegrityConstraints.WOR-150',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 60000;

            IF @AppLockResult < 0
                THROW 51000, 'WOR-150 database integrity migration could not acquire the schema lock.', 1;

            IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
                THROW 51000, 'WOR-150 database integrity migration requires dbo.Users.', 1;
            IF OBJECT_ID(N'dbo.PushSubscriptions', N'U') IS NULL
                THROW 51000, 'WOR-150 database integrity migration requires dbo.PushSubscriptions.', 1;
            IF OBJECT_ID(N'dbo.NotificationQueue', N'U') IS NULL
                THROW 51000, 'WOR-150 database integrity migration requires dbo.NotificationQueue.', 1;
            IF OBJECT_ID(N'dbo.JobViews', N'U') IS NULL
                THROW 51000, 'WOR-150 database integrity migration requires dbo.JobViews.', 1;
            IF OBJECT_ID(N'dbo.Worksheets', N'U') IS NULL
                THROW 51000, 'WOR-150 database integrity migration requires dbo.Worksheets.', 1;
            IF OBJECT_ID(N'dbo.JobReports', N'U') IS NULL
                THROW 51000, 'WOR-150 database integrity migration requires dbo.JobReports.', 1;
            IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
                THROW 51000, 'WOR-150 database integrity migration requires dbo.Customers.', 1;

            DECLARE @InvalidCount int;
            DECLARE @ErrorMessage nvarchar(2048);

            SELECT @InvalidCount = COUNT(*)
            FROM dbo.PushSubscriptions AS subscription
            LEFT JOIN dbo.Users AS appUser ON appUser.Id = subscription.UserId
            WHERE appUser.Id IS NULL;

            IF @InvalidCount > 0
            BEGIN
                SET @ErrorMessage = CONCAT(
                    'WOR-150 cannot add FK_PushSubscriptions_Users_UserId: found ',
                    @InvalidCount,
                    ' PushSubscriptions row(s) with a missing UserId.');
                THROW 51001, @ErrorMessage, 1;
            END;

            SELECT @InvalidCount = COUNT(*)
            FROM dbo.NotificationQueue AS notification
            LEFT JOIN dbo.Users AS appUser ON appUser.Id = notification.UserId
            WHERE appUser.Id IS NULL;

            IF @InvalidCount > 0
            BEGIN
                SET @ErrorMessage = CONCAT(
                    'WOR-150 cannot add FK_NotificationQueue_Users_UserId: found ',
                    @InvalidCount,
                    ' NotificationQueue row(s) with a missing UserId.');
                THROW 51002, @ErrorMessage, 1;
            END;

            SELECT @InvalidCount = COUNT(*)
            FROM dbo.JobViews AS jobView
            LEFT JOIN dbo.Users AS appUser ON appUser.Id = jobView.UserId
            WHERE appUser.Id IS NULL;

            IF @InvalidCount > 0
            BEGIN
                SET @ErrorMessage = CONCAT(
                    'WOR-150 cannot add FK_JobViews_Users_UserId: found ',
                    @InvalidCount,
                    ' JobViews row(s) with a missing UserId.');
                THROW 51003, @ErrorMessage, 1;
            END;

            SELECT @InvalidCount = COUNT(*)
            FROM dbo.Worksheets AS worksheet
            LEFT JOIN dbo.JobReports AS job
                ON job.OrganizationId = worksheet.OrganizationId
                AND job.Id = worksheet.JobId
            WHERE job.Id IS NULL;

            IF @InvalidCount > 0
            BEGIN
                SET @ErrorMessage = CONCAT(
                    'WOR-150 cannot add the tenant-scoped Worksheets-to-JobReports FK: found ',
                    @InvalidCount,
                    ' orphaned or cross-tenant worksheet job reference(s).');
                THROW 51004, @ErrorMessage, 1;
            END;

            SELECT @InvalidCount = COUNT(*)
            FROM dbo.Worksheets AS worksheet
            LEFT JOIN dbo.Users AS appUser
                ON appUser.OrganizationId = worksheet.OrganizationId
                AND appUser.Id = worksheet.UserId
            WHERE appUser.Id IS NULL;

            IF @InvalidCount > 0
            BEGIN
                SET @ErrorMessage = CONCAT(
                    'WOR-150 cannot add the tenant-scoped Worksheets-to-Users FK: found ',
                    @InvalidCount,
                    ' orphaned or cross-tenant worksheet user reference(s).');
                THROW 51005, @ErrorMessage, 1;
            END;

            SELECT @InvalidCount = COUNT(*)
            FROM dbo.JobReports AS job
            LEFT JOIN dbo.Customers AS customer
                ON customer.OrganizationId = job.OrganizationId
                AND customer.Id = job.CustomerId
            WHERE job.CustomerId IS NOT NULL
                AND customer.Id IS NULL;

            IF @InvalidCount > 0
            BEGIN
                SET @ErrorMessage = CONCAT(
                    'WOR-150 cannot add the tenant-scoped JobReports-to-Customers FK: found ',
                    @InvalidCount,
                    ' orphaned or cross-tenant customer reference(s).');
                THROW 51006, @ErrorMessage, 1;
            END;

            IF NOT EXISTS
            (
                SELECT 1
                FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.Users')
                    AND name IN (N'AK_Users_OrganizationId_Id', N'UX_Users_Organization_Id')
                    AND is_unique = 1
            )
                CREATE UNIQUE INDEX UX_Users_Organization_Id
                    ON dbo.Users (OrganizationId, Id);

            IF NOT EXISTS
            (
                SELECT 1
                FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.JobReports')
                    AND name IN (N'AK_JobReports_OrganizationId_Id', N'UX_JobReports_Organization_Id')
                    AND is_unique = 1
            )
                CREATE UNIQUE INDEX UX_JobReports_Organization_Id
                    ON dbo.JobReports (OrganizationId, Id);

            IF NOT EXISTS
            (
                SELECT 1
                FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.Customers')
                    AND name IN (N'AK_Customers_OrganizationId_Id', N'UX_Customers_Organization_Id')
                    AND is_unique = 1
            )
                CREATE UNIQUE INDEX UX_Customers_Organization_Id
                    ON dbo.Customers (OrganizationId, Id);

            IF OBJECT_ID(N'dbo.FK_Worksheets_JobReports_JobId', N'F') IS NOT NULL
                ALTER TABLE dbo.Worksheets
                    DROP CONSTRAINT FK_Worksheets_JobReports_JobId;

            IF OBJECT_ID(N'dbo.FK_Worksheets_Users_UserId', N'F') IS NOT NULL
                ALTER TABLE dbo.Worksheets
                    DROP CONSTRAINT FK_Worksheets_Users_UserId;

            IF OBJECT_ID(N'dbo.FK_JobReports_Customers_CustomerId', N'F') IS NOT NULL
                ALTER TABLE dbo.JobReports
                    DROP CONSTRAINT FK_JobReports_Customers_CustomerId;

            IF OBJECT_ID(N'dbo.FK_PushSubscriptions_Users_UserId', N'F') IS NULL
            BEGIN
                ALTER TABLE dbo.PushSubscriptions WITH CHECK
                    ADD CONSTRAINT FK_PushSubscriptions_Users_UserId
                    FOREIGN KEY (UserId) REFERENCES dbo.Users (Id)
                    ON DELETE NO ACTION;
                ALTER TABLE dbo.PushSubscriptions
                    CHECK CONSTRAINT FK_PushSubscriptions_Users_UserId;
            END;

            IF OBJECT_ID(N'dbo.FK_NotificationQueue_Users_UserId', N'F') IS NULL
            BEGIN
                ALTER TABLE dbo.NotificationQueue WITH CHECK
                    ADD CONSTRAINT FK_NotificationQueue_Users_UserId
                    FOREIGN KEY (UserId) REFERENCES dbo.Users (Id)
                    ON DELETE NO ACTION;
                ALTER TABLE dbo.NotificationQueue
                    CHECK CONSTRAINT FK_NotificationQueue_Users_UserId;
            END;

            IF OBJECT_ID(N'dbo.FK_JobViews_Users_UserId', N'F') IS NULL
            BEGIN
                ALTER TABLE dbo.JobViews WITH CHECK
                    ADD CONSTRAINT FK_JobViews_Users_UserId
                    FOREIGN KEY (UserId) REFERENCES dbo.Users (Id)
                    ON DELETE NO ACTION;
                ALTER TABLE dbo.JobViews
                    CHECK CONSTRAINT FK_JobViews_Users_UserId;
            END;

            IF OBJECT_ID(N'dbo.FK_Worksheets_JobReports_OrganizationId_JobId', N'F') IS NULL
            BEGIN
                ALTER TABLE dbo.Worksheets WITH CHECK
                    ADD CONSTRAINT FK_Worksheets_JobReports_OrganizationId_JobId
                    FOREIGN KEY (OrganizationId, JobId)
                    REFERENCES dbo.JobReports (OrganizationId, Id)
                    ON DELETE NO ACTION;
                ALTER TABLE dbo.Worksheets
                    CHECK CONSTRAINT FK_Worksheets_JobReports_OrganizationId_JobId;
            END;

            IF OBJECT_ID(N'dbo.FK_Worksheets_Users_OrganizationId_UserId', N'F') IS NULL
            BEGIN
                ALTER TABLE dbo.Worksheets WITH CHECK
                    ADD CONSTRAINT FK_Worksheets_Users_OrganizationId_UserId
                    FOREIGN KEY (OrganizationId, UserId)
                    REFERENCES dbo.Users (OrganizationId, Id)
                    ON DELETE NO ACTION;
                ALTER TABLE dbo.Worksheets
                    CHECK CONSTRAINT FK_Worksheets_Users_OrganizationId_UserId;
            END;

            IF OBJECT_ID(N'dbo.FK_JobReports_Customers_OrganizationId_CustomerId', N'F') IS NULL
            BEGIN
                ALTER TABLE dbo.JobReports WITH CHECK
                    ADD CONSTRAINT FK_JobReports_Customers_OrganizationId_CustomerId
                    FOREIGN KEY (OrganizationId, CustomerId)
                    REFERENCES dbo.Customers (OrganizationId, Id)
                    ON DELETE NO ACTION;
                ALTER TABLE dbo.JobReports
                    CHECK CONSTRAINT FK_JobReports_Customers_OrganizationId_CustomerId;
            END;

            COMMIT TRANSACTION;
        END TRY
        BEGIN CATCH
            IF @@TRANCOUNT > 0
                ROLLBACK TRANSACTION;
            THROW;
        END CATCH;
        """;
}
