-- WOR-348: one-time production cleanup before first customer go-live.
--
-- This script is intentionally fail-closed and must be run through sqlcmd with
-- explicit variables. It never deletes Users, Customers, Organizations, or
-- installation/reference/identity/migration state retained by the go-live policy.
--
-- Dry run example:
--   sqlcmd ... -v ExpectedDatabaseName="<production-db>" -v ExpectedJobCount="-1" -v Execute="0" -i cleanup-prelive-orders.sql
--
-- Execute only after reviewing the dry-run count and verifying rollback/PITR:
--   sqlcmd ... -v ExpectedDatabaseName="<production-db>" -v ExpectedJobCount="<dry-run-count>" -v Execute="1" -i cleanup-prelive-orders.sql

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ExpectedDatabaseName sysname = N'$(ExpectedDatabaseName)';
DECLARE @ExpectedJobCountRaw nvarchar(30) = N'$(ExpectedJobCount)';
DECLARE @ExecuteRaw nvarchar(10) = N'$(Execute)';
DECLARE @ExpectedJobCount int = TRY_CONVERT(int, @ExpectedJobCountRaw);
DECLARE @Execute bit;

-- Build the unexpanded sqlcmd sentinels through concatenation so sqlcmd cannot
-- substitute the guard value itself. Comparing directly with N'$(Variable)'
-- would compare the substituted value to itself and always fail.
IF NULLIF(LTRIM(RTRIM(@ExpectedDatabaseName)), N'') IS NULL
   OR @ExpectedDatabaseName = N'$' + N'(ExpectedDatabaseName)'
    THROW 51000, 'ExpectedDatabaseName must be supplied through sqlcmd -v.', 1;

IF @ExpectedJobCountRaw = N'$' + N'(ExpectedJobCount)'
   OR @ExpectedJobCount IS NULL
    THROW 51001, 'ExpectedJobCount must be supplied through sqlcmd -v. Use -1 for dry-run discovery only.', 1;

IF @ExecuteRaw = N'$' + N'(Execute)'
   OR @ExecuteRaw NOT IN (N'0', N'1')
    THROW 51002, 'Execute must be supplied through sqlcmd -v as exactly 0 or 1.', 1;

SET @Execute = CONVERT(bit, @ExecuteRaw);

IF DB_NAME() <> @ExpectedDatabaseName
    THROW 51003, 'Connected database does not match ExpectedDatabaseName.', 1;

IF OBJECT_ID(N'dbo.JobReports', N'U') IS NULL
    THROW 51004, 'dbo.JobReports does not exist in the connected database.', 1;

IF @Execute = 1 AND @ExpectedJobCount < 0
    THROW 51005, 'Execute=1 requires the exact non-negative JobReports count observed in the immediately preceding dry run.', 1;

-- Exact preserved-identity snapshots. These are never emitted; they are used
-- only to prove the cleanup did not replace, delete, move, or alter retained
-- user/customer identity bindings or reference-data keys.
CREATE TABLE #OrganizationsBefore
(
    Id uniqueidentifier NOT NULL PRIMARY KEY
);
INSERT INTO #OrganizationsBefore (Id)
SELECT Id FROM dbo.Organizations;

CREATE TABLE #UsersBefore
(
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    OrganizationId uniqueidentifier NOT NULL,
    Email nvarchar(320) NULL,
    Role nvarchar(80) NOT NULL,
    EntraId nvarchar(80) NULL,
    EntraEmail nvarchar(200) NULL
);
INSERT INTO #UsersBefore (Id, OrganizationId, Email, Role, EntraId, EntraEmail)
SELECT Id, OrganizationId, Email, Role, EntraId, EntraEmail
FROM dbo.Users;

CREATE TABLE #CustomersBefore
(
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    OrganizationId uniqueidentifier NOT NULL,
    CustomerNumber nvarchar(100) NULL
);
INSERT INTO #CustomersBefore (Id, OrganizationId, CustomerNumber)
SELECT Id, OrganizationId, CustomerNumber
FROM dbo.Customers;

CREATE TABLE #InstallationDefinitionsBefore
(
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    OrganizationId uniqueidentifier NOT NULL
);
INSERT INTO #InstallationDefinitionsBefore (Id, OrganizationId)
SELECT Id, OrganizationId FROM dbo.InstallationTypeDefinitions;

CREATE TABLE #ControlCategoriesBefore
(
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    OrganizationId uniqueidentifier NOT NULL
);
INSERT INTO #ControlCategoriesBefore (Id, OrganizationId)
SELECT Id, OrganizationId FROM dbo.ControlCategories;

CREATE TABLE #ControlPointsBefore
(
    Id uniqueidentifier NOT NULL PRIMARY KEY,
    OrganizationId uniqueidentifier NOT NULL
);
INSERT INTO #ControlPointsBefore (Id, OrganizationId)
SELECT Id, OrganizationId FROM dbo.ControlPoints;

CREATE TABLE #InstallationMappingsBefore
(
    InstallationTypeDefinitionId uniqueidentifier NOT NULL,
    ControlCategoryId uniqueidentifier NOT NULL,
    ControlPointId uniqueidentifier NOT NULL,
    PRIMARY KEY (InstallationTypeDefinitionId, ControlCategoryId, ControlPointId)
);
INSERT INTO #InstallationMappingsBefore (InstallationTypeDefinitionId, ControlCategoryId, ControlPointId)
SELECT InstallationTypeDefinitionId, ControlCategoryId, ControlPointId
FROM dbo.InstallationTypeDefinitionMappings;

-- Additional fixed KEEP tables are protected by before/after row counts. Some are
-- rollout-dependent (for example OrganizationFilials), so only existing tables are
-- included. No row contents are emitted or copied outside the SQL session.
CREATE TABLE #AdditionalKeepCountsBefore
(
    TableName sysname NOT NULL PRIMARY KEY,
    ProtectedRowCount bigint NOT NULL
);

DECLARE @KeepTable sysname;
DECLARE @KeepSql nvarchar(max);

DECLARE keep_before_cursor CURSOR LOCAL FAST_FORWARD FOR
SELECT keep.TableName
FROM
(
    VALUES
        (N'OrganizationFilials'),
        (N'JobWorkKinds'),
        (N'JobClosureFlags'),
        (N'PushSubscriptions'),
        (N'InviteTokens'),
        (N'WorkslipSchemaMigrations'),
        (N'__EFMigrationsHistory')
) AS keep(TableName)
WHERE OBJECT_ID(N'dbo.' + keep.TableName, N'U') IS NOT NULL
ORDER BY keep.TableName;

OPEN keep_before_cursor;
FETCH NEXT FROM keep_before_cursor INTO @KeepTable;

WHILE @@FETCH_STATUS = 0
BEGIN
    SET @KeepSql =
        N'INSERT INTO #AdditionalKeepCountsBefore (TableName, ProtectedRowCount) ' +
        N'SELECT N''' + REPLACE(@KeepTable, N'''', N'''''') + N''', COUNT_BIG(*) FROM dbo.' +
        QUOTENAME(@KeepTable) + N';';

    EXEC sys.sp_executesql @KeepSql;
    FETCH NEXT FROM keep_before_cursor INTO @KeepTable;
END;

CLOSE keep_before_cursor;
DEALLOCATE keep_before_cursor;

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
    (SELECT COUNT(*) FROM #OrganizationsBefore) AS OrganizationsPreserved,
    (SELECT COUNT(*) FROM #UsersBefore) AS UsersPreserved,
    (SELECT COUNT(*) FROM #CustomersBefore) AS CustomersPreserved,
    (SELECT COUNT(*) FROM #InstallationDefinitionsBefore) AS InstallationDefinitionsPreserved,
    (SELECT COUNT(*) FROM #ControlCategoriesBefore) AS ControlCategoriesPreserved,
    (SELECT COUNT(*) FROM #ControlPointsBefore) AS ControlPointsPreserved,
    (SELECT COUNT(*) FROM #InstallationMappingsBefore) AS InstallationMappingsPreserved;

SELECT
    TableName AS AdditionalKeepTable,
    ProtectedRowCount AS PreservedRows
FROM #AdditionalKeepCountsBefore
ORDER BY TableName;

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

    IF EXISTS
    (
        SELECT 1
        FROM dbo.NotificationQueue AS notification
        JOIN #TargetJobs AS target
          ON ISJSON(notification.PayloadJson) = 1
         AND TRY_CONVERT(uniqueidentifier, JSON_VALUE(notification.PayloadJson, '$.jobId')) = target.Id
    )
        THROW 51016, 'Post-check failed: a notification still references a removed JobReport.', 1;

    IF OBJECT_ID(N'dbo.IdempotencyRecords', N'U') IS NOT NULL
       AND EXISTS
       (
           SELECT 1
           FROM dbo.IdempotencyRecords AS record
           JOIN #TargetJobs AS target
             ON record.Scope LIKE N'%' + CONVERT(nvarchar(36), target.Id) + N'%'
             OR record.[Key] LIKE N'%' + CONVERT(nvarchar(36), target.Id) + N'%'
             OR record.ResponseJson LIKE N'%' + CONVERT(nvarchar(36), target.Id) + N'%'
       )
        THROW 51017, 'Post-check failed: an idempotency record still references a removed JobReport.', 1;

    IF EXISTS
    (
        SELECT Id FROM #OrganizationsBefore
        EXCEPT
        SELECT Id FROM dbo.Organizations
    ) OR EXISTS
    (
        SELECT Id FROM dbo.Organizations
        EXCEPT
        SELECT Id FROM #OrganizationsBefore
    )
        THROW 51018, 'Safety check failed: Organization identities changed. Transaction will be rolled back.', 1;

    IF EXISTS
    (
        SELECT Id, OrganizationId, Email, Role, EntraId, EntraEmail FROM #UsersBefore
        EXCEPT
        SELECT Id, OrganizationId, Email, Role, EntraId, EntraEmail FROM dbo.Users
    ) OR EXISTS
    (
        SELECT Id, OrganizationId, Email, Role, EntraId, EntraEmail FROM dbo.Users
        EXCEPT
        SELECT Id, OrganizationId, Email, Role, EntraId, EntraEmail FROM #UsersBefore
    )
        THROW 51019, 'Safety check failed: retained User identities/roles/Entra bindings changed. Transaction will be rolled back.', 1;

    IF EXISTS
    (
        SELECT Id, OrganizationId, CustomerNumber FROM #CustomersBefore
        EXCEPT
        SELECT Id, OrganizationId, CustomerNumber FROM dbo.Customers
    ) OR EXISTS
    (
        SELECT Id, OrganizationId, CustomerNumber FROM dbo.Customers
        EXCEPT
        SELECT Id, OrganizationId, CustomerNumber FROM #CustomersBefore
    )
        THROW 51020, 'Safety check failed: retained Customer identities changed. Transaction will be rolled back.', 1;

    IF EXISTS
    (
        SELECT Id, OrganizationId FROM #InstallationDefinitionsBefore
        EXCEPT
        SELECT Id, OrganizationId FROM dbo.InstallationTypeDefinitions
    ) OR EXISTS
    (
        SELECT Id, OrganizationId FROM dbo.InstallationTypeDefinitions
        EXCEPT
        SELECT Id, OrganizationId FROM #InstallationDefinitionsBefore
    )
        THROW 51021, 'Safety check failed: installation definition identities changed. Transaction will be rolled back.', 1;

    IF EXISTS
    (
        SELECT Id, OrganizationId FROM #ControlCategoriesBefore
        EXCEPT
        SELECT Id, OrganizationId FROM dbo.ControlCategories
    ) OR EXISTS
    (
        SELECT Id, OrganizationId FROM dbo.ControlCategories
        EXCEPT
        SELECT Id, OrganizationId FROM #ControlCategoriesBefore
    )
        THROW 51022, 'Safety check failed: control category identities changed. Transaction will be rolled back.', 1;

    IF EXISTS
    (
        SELECT Id, OrganizationId FROM #ControlPointsBefore
        EXCEPT
        SELECT Id, OrganizationId FROM dbo.ControlPoints
    ) OR EXISTS
    (
        SELECT Id, OrganizationId FROM dbo.ControlPoints
        EXCEPT
        SELECT Id, OrganizationId FROM #ControlPointsBefore
    )
        THROW 51023, 'Safety check failed: control point identities changed. Transaction will be rolled back.', 1;

    IF EXISTS
    (
        SELECT InstallationTypeDefinitionId, ControlCategoryId, ControlPointId FROM #InstallationMappingsBefore
        EXCEPT
        SELECT InstallationTypeDefinitionId, ControlCategoryId, ControlPointId FROM dbo.InstallationTypeDefinitionMappings
    ) OR EXISTS
    (
        SELECT InstallationTypeDefinitionId, ControlCategoryId, ControlPointId FROM dbo.InstallationTypeDefinitionMappings
        EXCEPT
        SELECT InstallationTypeDefinitionId, ControlCategoryId, ControlPointId FROM #InstallationMappingsBefore
    )
        THROW 51024, 'Safety check failed: installation mappings changed. Transaction will be rolled back.', 1;

    CREATE TABLE #AdditionalKeepCountsAfter
    (
        TableName sysname NOT NULL PRIMARY KEY,
        ProtectedRowCount bigint NOT NULL
    );

    DECLARE keep_after_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT keep.TableName
    FROM
    (
        VALUES
            (N'OrganizationFilials'),
            (N'JobWorkKinds'),
            (N'JobClosureFlags'),
            (N'PushSubscriptions'),
            (N'InviteTokens'),
            (N'WorkslipSchemaMigrations'),
            (N'__EFMigrationsHistory')
    ) AS keep(TableName)
    WHERE OBJECT_ID(N'dbo.' + keep.TableName, N'U') IS NOT NULL
    ORDER BY keep.TableName;

    OPEN keep_after_cursor;
    FETCH NEXT FROM keep_after_cursor INTO @KeepTable;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @KeepSql =
            N'INSERT INTO #AdditionalKeepCountsAfter (TableName, ProtectedRowCount) ' +
            N'SELECT N''' + REPLACE(@KeepTable, N'''', N'''''') + N''', COUNT_BIG(*) FROM dbo.' +
            QUOTENAME(@KeepTable) + N';';

        EXEC sys.sp_executesql @KeepSql;
        FETCH NEXT FROM keep_after_cursor INTO @KeepTable;
    END;

    CLOSE keep_after_cursor;
    DEALLOCATE keep_after_cursor;

    IF EXISTS
    (
        SELECT TableName, ProtectedRowCount FROM #AdditionalKeepCountsBefore
        EXCEPT
        SELECT TableName, ProtectedRowCount FROM #AdditionalKeepCountsAfter
    ) OR EXISTS
    (
        SELECT TableName, ProtectedRowCount FROM #AdditionalKeepCountsAfter
        EXCEPT
        SELECT TableName, ProtectedRowCount FROM #AdditionalKeepCountsBefore
    )
        THROW 51025, 'Safety check failed: a protected KEEP table row count changed. Transaction will be rolled back.', 1;

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
