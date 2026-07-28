namespace Workslip.Infrastructure.Schema;

internal static class Wor160SchemaMigrationSql
{
    internal const string Apply = """
        SET XACT_ABORT ON;

        BEGIN TRANSACTION;
        BEGIN TRY
            DECLARE @AppLockResult int;
            EXECUTE @AppLockResult = sys.sp_getapplock
                @Resource = N'Workslip.DatabaseIntegrityConstraints.WOR-160',
                @LockMode = N'Exclusive',
                @LockOwner = N'Transaction',
                @LockTimeout = 60000;

            IF @AppLockResult < 0
                THROW 51000, 'WOR-160 schema migration could not acquire the schema lock.', 1;

            IF OBJECT_ID(N'dbo.JobReportInstallationCategories', N'U') IS NULL
                THROW 51000, 'WOR-160 schema migration requires dbo.JobReportInstallationCategories.', 1;
            IF OBJECT_ID(N'dbo.JobReportInstallations', N'U') IS NULL
                THROW 51000, 'WOR-160 schema migration requires dbo.JobReportInstallations.', 1;
            IF OBJECT_ID(N'dbo.ControlCategories', N'U') IS NULL
                THROW 51000, 'WOR-160 schema migration requires dbo.ControlCategories.', 1;

            DECLARE @InvalidCount int;
            DECLARE @ErrorMessage nvarchar(2048);

            -- Validate no cross-tenant references exist before schema change
            SELECT @InvalidCount = COUNT(*)
            FROM dbo.JobReportInstallationCategories AS cat
            INNER JOIN dbo.JobReportInstallations AS inst
                ON inst.Id = cat.JobReportInstallationId
            WHERE inst.OrganizationId != (
                SELECT cc.OrganizationId
                FROM dbo.ControlCategories cc
                WHERE cc.Id = cat.ControlCategoryId
            );

            IF @InvalidCount > 0
            BEGIN
                SET @ErrorMessage = CONCAT(
                    'WOR-160 cannot add tenant-scoped category FK: found ',
                    @InvalidCount,
                    ' cross-tenant installation category reference(s).');
                THROW 51001, @ErrorMessage, 1;
            END;

            -- Add OrganizationId column (nullable first)
            IF COL_LENGTH(N'dbo.JobReportInstallationCategories', N'OrganizationId') IS NULL
                ALTER TABLE dbo.JobReportInstallationCategories ADD [OrganizationId] uniqueidentifier NULL;

            -- Backfill from parent installation
            UPDATE cat
            SET cat.OrganizationId = inst.OrganizationId
            FROM dbo.JobReportInstallationCategories AS cat
            INNER JOIN dbo.JobReportInstallations AS inst
                ON inst.Id = cat.JobReportInstallationId
            WHERE cat.OrganizationId IS NULL;

            -- Make NOT NULL
            ALTER TABLE dbo.JobReportInstallationCategories
                ALTER COLUMN [OrganizationId] uniqueidentifier NOT NULL;

            -- Drop old single-column FKs if they exist
            IF OBJECT_ID(N'dbo.FK_JobReportInstallationCategories_JobReportInstallations_JobReportInstallationId', N'F') IS NOT NULL
                ALTER TABLE dbo.JobReportInstallationCategories
                    DROP CONSTRAINT FK_JobReportInstallationCategories_JobReportInstallations_JobReportInstallationId;

            IF OBJECT_ID(N'dbo.FK_JobReportInstallationCategories_ControlCategories_ControlCategoryId', N'F') IS NOT NULL
                ALTER TABLE dbo.JobReportInstallationCategories
                    DROP CONSTRAINT FK_JobReportInstallationCategories_ControlCategories_ControlCategoryId;

            -- Add composite FK to parent installation
            IF OBJECT_ID(N'dbo.FK_JobReportInstallationCategories_JobReportInstallations_OrgId_InstId', N'F') IS NULL
            BEGIN
                ALTER TABLE dbo.JobReportInstallationCategories WITH CHECK
                    ADD CONSTRAINT FK_JobReportInstallationCategories_JobReportInstallations_OrgId_InstId
                    FOREIGN KEY (OrganizationId, JobReportInstallationId)
                    REFERENCES dbo.JobReportInstallations (OrganizationId, Id)
                    ON DELETE CASCADE;
                ALTER TABLE dbo.JobReportInstallationCategories
                    CHECK CONSTRAINT FK_JobReportInstallationCategories_JobReportInstallations_OrgId_InstId;
            END;

            -- Add composite FK to ControlCategories
            IF OBJECT_ID(N'dbo.FK_JobReportInstallationCategories_ControlCategories_OrgId_CatId', N'F') IS NULL
            BEGIN
                ALTER TABLE dbo.JobReportInstallationCategories WITH CHECK
                    ADD CONSTRAINT FK_JobReportInstallationCategories_ControlCategories_OrgId_CatId
                    FOREIGN KEY (OrganizationId, ControlCategoryId)
                    REFERENCES dbo.ControlCategories (OrganizationId, Id)
                    ON DELETE NO ACTION;
                ALTER TABLE dbo.JobReportInstallationCategories
                    CHECK CONSTRAINT FK_JobReportInstallationCategories_ControlCategories_OrgId_CatId;
            END;

            -- Create unique index (idempotent)
            IF NOT EXISTS
            (
                SELECT 1
                FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'dbo.JobReportInstallationCategories')
                    AND name = N'UX_JobReportInstallationCategories_Org_Inst_Cat'
                    AND is_unique = 1
            )
                CREATE UNIQUE INDEX UX_JobReportInstallationCategories_Org_Inst_Cat
                    ON dbo.JobReportInstallationCategories (OrganizationId, JobReportInstallationId, ControlCategoryId);

            COMMIT TRANSACTION;
        END TRY
        BEGIN CATCH
            IF @@TRANCOUNT > 0
                ROLLBACK TRANSACTION;
            THROW;
        END CATCH;
        """;
}
