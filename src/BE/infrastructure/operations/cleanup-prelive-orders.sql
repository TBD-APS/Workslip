-- WOR-348: one-time production cleanup before first customer go-live.
--
-- This script is intentionally fail-closed and must be run through sqlcmd with
-- explicit variables. It never deletes Users, Customers, Organizations, or
-- installation reference data.
--
-- Dry run example:
--   sqlcmd ... -v ExpectedDatabaseName="<production-db>" ExpectedJobCount="-1" Execute="0" -i cleanup-prelive-orders.sql
--
-- Execute only after reviewing the dry-run count and verifying rollback/PITR:
--   sqlcmd ... -v ExpectedDatabaseName="<production-db>" ExpectedJobCount="<dry-run-count>" Execute="1" -i cleanup-prelive-orders.sql

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ExpectedDatabaseName sysname = N'$(ExpectedDatabaseName)';
DECLARE @ExpectedJobCount int = TRY_CONVERT(int, N'$(ExpectedJobCount)');
DECLARE @Execute bit = TRY_CONVERT(bit, N'$(Execute)');

IF @ExpectedDatabaseName = N'$(ExpectedDatabaseName)' OR NULLIF(LTRIM(RTRIM(@ExpectedDatabaseName)), N'') IS NULL
    THROW 51000, 'ExpectedDatabaseName must be supplied through sqlcmd -v.', 1;

IF @ExpectedJobCount IS NULL
    THROW 51001, 'ExpectedJobCount must be supplied through sqlcmd -v. Use -1 for dry-run discovery only.', 1;

IF @Execute IS NULL
    THROW 51002, 'Execute must be supplied through sqlcmd -v as 0 or 1.', 1;

IF DB_NAME() <> @ExpectedDatabaseName
    THROW 51003, 'Connected database does not match ExpectedDatabaseName.', 1;

IF OBJECT_ID(N'dbo.JobReports', N'U') IS NULL
    THROW 51004, 'dbo.JobReports does not exist in the connected database.', 1;

IF @Execute = 1 AND @ExpectedJobCount < 0
    THROW 51005, 'Execute=1 requires the exact non-negative JobReports count observed in the immediately preceding dry run.', 1;

DECLARE @OrganizationCountBefore int = (SELECT COUNT(*) FROM dbo.Organizations);
DECLARE @UserCountBefore int = (SELECT COUNT(*) FROM dbo.Users);
DECLARE @CustomerCountBefore int = (SELECT COUNT(*) FROM dbo.Customers);
DECLARE @InstallationDefinitionCountBefore int = (SELECT COUNT(*) FROM dbo.InstallationTypeDefinitions);
DECLARE @ControlCategoryCountBefore int = (SELECT COUNT(*) FROM dbo.ControlCategories);
DECLARE @ControlPointCountBefore int = (SELECT COUNT(*) FROM dbo.ControlPoints);
DECLARE @InstallationMappingCountBefore int = (SELECT COUNT(*) FROM dbo.InstallationTypeDefinitionMappings);

CREATE TABLE #TargetJobs
(
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    OrganizationId uniqueidentifier NOT NULL
);

INSERT INTO #TargetJobs (Id, OrganizationId)
SELECT Id, OrganizationId
FROM dbo.JobReports;

DECLARE @JobCount int = (SELECT COUNT(*) FROM #TargetJobs);

CREATE TABLE #TargetNotifications
(
    Id uniqueidentifier NOT NULL PRIMARY KEY
);

INSERT INTO #TargetNotifications (Id)
SELECT DISTINCT notification.Id
FROM dbo.NotificationQueue AS notification
JOIN #TargetJobs AS target
    ON ISJSON(notification.PayloadJson) = 1
   AND TRY_CONVERT(uniqueidentifier, JSON_VALUE(notification.PayloadJson, '$.jobId')) = target.Id;

CREATE TABLE #TargetIdempotencyRecords
(
    Id uniqueidentifier NOT NULL PRIMARY KEY
);

IF OBJECT_ID(N'dbo.IdempotencyRecords', N'U') IS NOT NULL
BEGIN
    INSERT INTO #TargetIdempotencyRecords (Id)
    SELECT DISTINCT record.Id
    FROM dbo.IdempotencyRecords AS record
    JOIN #TargetJobs AS target
      ON record.Scope LIKE N'%' + CONVERT(nvarchar(36), target.Id) + N'%'
      OR record.[Key] LIKE N'%' + CONVERT(nvarchar(36), target.Id) + N'%'
      OR record.ResponseJson LIKE N'%' + CONVERT(nvarchar(36), target.Id) + N'%';
END;

SELECT
    DB_NAME() AS DatabaseName,
    CAST(@Execute AS int) AS ExecuteMode,
    @JobCount AS JobReports,
    (SELECT COUNT(*) FROM dbo.Worksheets AS row JOIN #TargetJobs AS target ON row.JobId = target.Id AND row.OrganizationId = target.OrganizationId) AS Worksheets,
    (SELECT COUNT(*) FROM dbo.JobAssignments AS row JOIN #TargetJobs AS target ON row.ReportId = target.Id AND row.OrganizationId = target.OrganizationId) AS JobAssignments,
    (SELECT COUNT(*) FROM dbo.JobReportLinks AS row WHERE EXISTS (SELECT 1 FROM #TargetJobs AS target WHERE target.OrganizationId = row.OrganizationId AND (target.Id = row.SourceReportId OR target.Id = row.TargetReportId))) AS JobReportLinks,
    (SELECT COUNT(*) FROM dbo.JobEvents AS row JOIN #TargetJobs AS target ON row.ReportId = target.Id AND row.OrganizationId = target.OrganizationId) AS JobEvents,
    (SELECT COUNT(*) FROM dbo.JobReportClosureFlags AS row JOIN #TargetJobs AS target ON row.JobReportId = target.Id AND row.OrganizationId = target.OrganizationId) AS JobReportClosureFlags,
    (SELECT COUNT(*) FROM dbo.JobReportInstallations AS row JOIN #TargetJobs AS target ON row.JobReportId = target.Id AND row.OrganizationId = target.OrganizationId) AS JobReportInstallations,
    (SELECT COUNT(*) FROM dbo.JobViews AS row JOIN #TargetJobs AS target ON row.JobId = target.Id) AS JobViews,
    (SELECT COUNT(*) FROM #TargetNotifications) AS NotificationQueueRows,
    (SELECT COUNT(*) FROM dbo.NotificationDeliveryLog AS row JOIN #TargetNotifications AS target ON row.NotificationId = target.Id) AS NotificationDeliveryLogRows,
    (SELECT COUNT(*) FROM #TargetIdempotencyRecords) AS IdempotencyRecordsReferencingJobs,
    @OrganizationCountBefore AS OrganizationsPreserved,
    @UserCountBefore AS UsersPreserved,
    @CustomerCountBefore AS CustomersPreserved,
    @InstallationDefinitionCountBefore AS InstallationDefinitionsPreserved,
    @ControlCategoryCountBefore AS ControlCategoriesPreserved,
    @ControlPointCountBefore AS ControlPointsPreserved,
    @InstallationMappingCountBefore AS InstallationMappingsPreserved;

IF @Execute = 0
BEGIN
    PRINT 'DRY RUN ONLY: no rows were changed.';
    RETURN;
END;

IF @JobCount <> @ExpectedJobCount
    THROW 51006, 'JobReports count changed since dry run. Abort and review a new dry run before executing.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Prevent concurrent case creation/update while the target set is being removed.
    -- The API/background workers should also be stopped during the maintenance window.
    SELECT COUNT_BIG(*) AS LockedJobReportCount
    FROM dbo.JobReports WITH (TABLOCKX, HOLDLOCK);

    IF (SELECT COUNT(*) FROM dbo.JobReports) <> @ExpectedJobCount
        THROW 51007, 'JobReports count changed while acquiring the cleanup lock. Transaction will be rolled back.', 1;

    DELETE delivery
    FROM dbo.NotificationDeliveryLog AS delivery
    JOIN #TargetNotifications AS target ON delivery.NotificationId = target.Id;

    DELETE notification
    FROM dbo.NotificationQueue AS notification
    JOIN #TargetNotifications AS target ON notification.Id = target.Id;

    IF OBJECT_ID(N'dbo.IdempotencyRecords', N'U') IS NOT NULL
    BEGIN
        DELETE record
        FROM dbo.IdempotencyRecords AS record
        JOIN #TargetIdempotencyRecords AS target ON record.Id = target.Id;
    END;

    -- Worksheets and report links use Restrict FKs and must be removed explicitly.
    DELETE worksheet
    FROM dbo.Worksheets AS worksheet
    JOIN #TargetJobs AS target
      ON worksheet.JobId = target.Id
     AND worksheet.OrganizationId = target.OrganizationId;

    DELETE link
    FROM dbo.JobReportLinks AS link
    WHERE EXISTS
    (
        SELECT 1
        FROM #TargetJobs AS target
        WHERE target.OrganizationId = link.OrganizationId
          AND (target.Id = link.SourceReportId OR target.Id = link.TargetReportId)
    );

    -- Current EF/SQL relationships cascade JobAssignments, JobEvents,
    -- JobReportClosureFlags, JobReportInstallations (including category/control
    -- point snapshots), and JobViews when the JobReport is deleted.
    DELETE report
    FROM dbo.JobReports AS report
    JOIN #TargetJobs AS target
      ON report.Id = target.Id
     AND report.OrganizationId = target.OrganizationId;

    IF EXISTS (SELECT 1 FROM dbo.JobReports)
        THROW 51008, 'Post-check failed: JobReports still contains rows.', 1;

    IF EXISTS (SELECT 1 FROM dbo.Worksheets AS row JOIN #TargetJobs AS target ON row.JobId = target.Id AND row.OrganizationId = target.OrganizationId)
        THROW 51009, 'Post-check failed: target Worksheets remain.', 1;

    IF EXISTS (SELECT 1 FROM dbo.JobAssignments AS row JOIN #TargetJobs AS target ON row.ReportId = target.Id AND row.OrganizationId = target.OrganizationId)
        THROW 51010, 'Post-check failed: target JobAssignments remain.', 1;

    IF EXISTS (SELECT 1 FROM dbo.JobReportLinks AS row WHERE EXISTS (SELECT 1 FROM #TargetJobs AS target WHERE target.OrganizationId = row.OrganizationId AND (target.Id = row.SourceReportId OR target.Id = row.TargetReportId)))
        THROW 51011, 'Post-check failed: target JobReportLinks remain.', 1;

    IF EXISTS (SELECT 1 FROM dbo.JobEvents AS row JOIN #TargetJobs AS target ON row.ReportId = target.Id AND row.OrganizationId = target.OrganizationId)
        THROW 51012, 'Post-check failed: target JobEvents remain.', 1;

    IF EXISTS (SELECT 1 FROM dbo.JobReportClosureFlags AS row JOIN #TargetJobs AS target ON row.JobReportId = target.Id AND row.OrganizationId = target.OrganizationId)
        THROW 51013, 'Post-check failed: target closure selections remain.', 1;

    IF EXISTS (SELECT 1 FROM dbo.JobReportInstallations AS row JOIN #TargetJobs AS target ON row.JobReportId = target.Id AND row.OrganizationId = target.OrganizationId)
        THROW 51014, 'Post-check failed: target installation snapshots remain.', 1;

    IF EXISTS (SELECT 1 FROM dbo.JobViews AS row JOIN #TargetJobs AS target ON row.JobId = target.Id)
        THROW 51015, 'Post-check failed: target JobViews remain.', 1;

    IF EXISTS (SELECT 1 FROM dbo.NotificationQueue AS row JOIN #TargetNotifications AS target ON row.Id = target.Id)
        THROW 51016, 'Post-check failed: target notification rows remain.', 1;

    IF (SELECT COUNT(*) FROM dbo.Organizations) <> @OrganizationCountBefore
        THROW 51017, 'Safety check failed: Organization count changed. Transaction will be rolled back.', 1;

    IF (SELECT COUNT(*) FROM dbo.Users) <> @UserCountBefore
        THROW 51018, 'Safety check failed: User count changed. Transaction will be rolled back.', 1;

    IF (SELECT COUNT(*) FROM dbo.Customers) <> @CustomerCountBefore
        THROW 51019, 'Safety check failed: Customer count changed. Transaction will be rolled back.', 1;

    IF (SELECT COUNT(*) FROM dbo.InstallationTypeDefinitions) <> @InstallationDefinitionCountBefore
        OR (SELECT COUNT(*) FROM dbo.ControlCategories) <> @ControlCategoryCountBefore
        OR (SELECT COUNT(*) FROM dbo.ControlPoints) <> @ControlPointCountBefore
        OR (SELECT COUNT(*) FROM dbo.InstallationTypeDefinitionMappings) <> @InstallationMappingCountBefore
        THROW 51020, 'Safety check failed: installation reference-data counts changed. Transaction will be rolled back.', 1;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT
    (SELECT COUNT(*) FROM dbo.JobReports) AS JobReportsAfter,
    (SELECT COUNT(*) FROM dbo.Users) AS UsersAfter,
    (SELECT COUNT(*) FROM dbo.Customers) AS CustomersAfter,
    (SELECT COUNT(*) FROM dbo.InstallationTypeDefinitions) AS InstallationDefinitionsAfter,
    (SELECT COUNT(*) FROM dbo.ControlCategories) AS ControlCategoriesAfter,
    (SELECT COUNT(*) FROM dbo.ControlPoints) AS ControlPointsAfter,
    (SELECT COUNT(*) FROM dbo.InstallationTypeDefinitionMappings) AS InstallationMappingsAfter;

PRINT 'WOR-348 cleanup committed. Continue with WOR-351 post-cleanup validation before go-live.';
