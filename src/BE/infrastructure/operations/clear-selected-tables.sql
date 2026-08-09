-- WOR-348: configurable whole-table cleanup helper for controlled go-live preparation.
--
-- IMPORTANT:
-- - This deletes ROWS, never tables, foreign keys, or schema.
-- - The existing cleanup-prelive-orders.sql remains the canonical WOR-348 cleanup
--   because it also handles job references stored outside foreign keys (notification
--   JSON and idempotency records) and verifies preserved identity/reference sets.
-- - Use this helper only when the intended cleanup target is the ENTIRE contents of
--   each selected table and all relevant relationships are represented by foreign keys.
--
-- Tables are supplied as a semicolon-separated sqlcmd variable:
--   TablesToClear="dbo.JobEvents;dbo.JobReports"
--
-- Dry run:
--   sqlcmd ... -v ExpectedDatabaseName="<db>" TablesToClear="dbo.TableA;dbo.TableB" ExpectedCountSignature="DISCOVER" Execute="0" -i clear-selected-tables.sql
--
-- Execute only after reviewing the immediately preceding dry-run output:
--   sqlcmd ... -v ExpectedDatabaseName="<db>" TablesToClear="dbo.TableA;dbo.TableB" ExpectedCountSignature="<dry-run-signature>" Execute="1" -i clear-selected-tables.sql

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ExpectedDatabaseName sysname = N'$(ExpectedDatabaseName)';
DECLARE @TablesRaw nvarchar(max) = N'$(TablesToClear)';
DECLARE @ExpectedCountSignature varchar(128) = '$(ExpectedCountSignature)';
DECLARE @ExecuteRaw nvarchar(10) = N'$(Execute)';
DECLARE @Execute bit;

IF @ExpectedDatabaseName = N'$(ExpectedDatabaseName)'
   OR NULLIF(LTRIM(RTRIM(@ExpectedDatabaseName)), N'') IS NULL
    THROW 51100, 'ExpectedDatabaseName must be supplied through sqlcmd -v.', 1;

IF DB_NAME() <> @ExpectedDatabaseName
    THROW 51101, 'Connected database does not match ExpectedDatabaseName.', 1;

IF @TablesRaw = N'$(TablesToClear)'
   OR NULLIF(LTRIM(RTRIM(@TablesRaw)), N'') IS NULL
    THROW 51102, 'TablesToClear must contain one or more semicolon-separated schema.table names.', 1;

IF @ExecuteRaw = N'$(Execute)' OR @ExecuteRaw NOT IN (N'0', N'1')
    THROW 51103, 'Execute must be supplied through sqlcmd -v as exactly 0 or 1.', 1;

SET @Execute = CONVERT(bit, @ExecuteRaw);

IF @ExpectedCountSignature = '$(ExpectedCountSignature)'
   OR NULLIF(LTRIM(RTRIM(@ExpectedCountSignature)), '') IS NULL
    THROW 51104, 'ExpectedCountSignature must be supplied. Use DISCOVER for dry-run.', 1;

IF @Execute = 0 AND UPPER(@ExpectedCountSignature) <> 'DISCOVER'
    THROW 51105, 'Execute=0 requires ExpectedCountSignature=DISCOVER.', 1;

IF @Execute = 1
   AND
   (
       LEN(@ExpectedCountSignature) <> 64
       OR @ExpectedCountSignature LIKE '%[^0-9A-Fa-f]%'
   )
    THROW 51106, 'Execute=1 requires the exact 64-character hexadecimal CountSignature from the immediately preceding dry run.', 1;

CREATE TABLE #RequestedTables
(
    RawName nvarchar(517) NOT NULL PRIMARY KEY,
    SchemaName sysname NULL,
    TableName sysname NULL
);

INSERT INTO #RequestedTables (RawName, SchemaName, TableName)
SELECT DISTINCT
    LTRIM(RTRIM(value)),
    PARSENAME(LTRIM(RTRIM(value)), 2),
    PARSENAME(LTRIM(RTRIM(value)), 1)
FROM STRING_SPLIT(@TablesRaw, N';')
WHERE NULLIF(LTRIM(RTRIM(value)), N'') IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM #RequestedTables)
    THROW 51107, 'TablesToClear did not contain any table names after parsing.', 1;

IF EXISTS
(
    SELECT 1
    FROM #RequestedTables
    WHERE SchemaName IS NULL
       OR TableName IS NULL
       OR PARSENAME(RawName, 3) IS NOT NULL
)
BEGIN
    SELECT RawName AS InvalidTableName
    FROM #RequestedTables
    WHERE SchemaName IS NULL
       OR TableName IS NULL
       OR PARSENAME(RawName, 3) IS NOT NULL
    ORDER BY RawName;

    THROW 51108, 'Every TablesToClear item must use exactly schema.table format.', 1;
END;

CREATE TABLE #Targets
(
    ObjectId int NOT NULL PRIMARY KEY,
    SchemaName sysname NOT NULL,
    TableName sysname NOT NULL,
    UNIQUE (SchemaName, TableName)
);

INSERT INTO #Targets (ObjectId, SchemaName, TableName)
SELECT
    t.object_id,
    s.name,
    t.name
FROM #RequestedTables AS requested
JOIN sys.schemas AS s
  ON s.name = requested.SchemaName
JOIN sys.tables AS t
  ON t.schema_id = s.schema_id
 AND t.name = requested.TableName;

IF (SELECT COUNT(*) FROM #Targets) <> (SELECT COUNT(*) FROM #RequestedTables)
BEGIN
    SELECT requested.RawName AS MissingTable
    FROM #RequestedTables AS requested
    LEFT JOIN #Targets AS target
      ON target.SchemaName = requested.SchemaName
     AND target.TableName = requested.TableName
    WHERE target.ObjectId IS NULL
    ORDER BY requested.RawName;

    THROW 51109, 'One or more requested tables do not exist in the connected database.', 1;
END;

-- These tables are explicitly retained by WOR-348 or are required schema/reference state.
-- This helper intentionally cannot be used to clear them.
DECLARE @ProtectedTables TABLE
(
    SchemaName sysname NOT NULL,
    TableName sysname NOT NULL,
    PRIMARY KEY (SchemaName, TableName)
);

INSERT INTO @ProtectedTables (SchemaName, TableName)
VALUES
    (N'dbo', N'Organizations'),
    (N'dbo', N'Users'),
    (N'dbo', N'Customers'),
    (N'dbo', N'InstallationTypeDefinitions'),
    (N'dbo', N'ControlCategories'),
    (N'dbo', N'ControlPoints'),
    (N'dbo', N'InstallationTypeDefinitionMappings'),
    (N'dbo', N'JobWorkKinds'),
    (N'dbo', N'JobClosureFlags'),
    (N'dbo', N'PushSubscriptions'),
    (N'dbo', N'InviteTokens'),
    (N'dbo', N'__EFMigrationsHistory');

IF EXISTS
(
    SELECT 1
    FROM #Targets AS target
    JOIN @ProtectedTables AS protected
      ON protected.SchemaName = target.SchemaName
     AND protected.TableName = target.TableName
)
BEGIN
    SELECT
        target.SchemaName,
        target.TableName
    FROM #Targets AS target
    JOIN @ProtectedTables AS protected
      ON protected.SchemaName = target.SchemaName
     AND protected.TableName = target.TableName
    ORDER BY target.SchemaName, target.TableName;

    THROW 51110, 'TablesToClear contains a table protected by the WOR-348 go-live retention rules.', 1;
END;

IF EXISTS
(
    SELECT 1
    FROM sys.tables AS t
    JOIN #Targets AS target ON target.ObjectId = t.object_id
    WHERE t.temporal_type <> 0
       OR t.is_tracked_by_cdc = 1
)
BEGIN
    SELECT
        target.SchemaName,
        target.TableName,
        t.temporal_type_desc AS TemporalType,
        t.is_tracked_by_cdc AS IsTrackedByCdc
    FROM sys.tables AS t
    JOIN #Targets AS target ON target.ObjectId = t.object_id
    WHERE t.temporal_type <> 0
       OR t.is_tracked_by_cdc = 1
    ORDER BY target.SchemaName, target.TableName;

    THROW 51111, 'Temporal or CDC-tracked tables require an explicit reviewed cleanup path and are not supported by this helper.', 1;
END;

-- DELETE triggers can mutate tables that are not in TablesToClear. Fail closed rather
-- than trying to infer trigger semantics.
IF EXISTS
(
    SELECT 1
    FROM sys.triggers AS trigger_row
    JOIN #Targets AS target ON target.ObjectId = trigger_row.parent_id
    WHERE trigger_row.is_disabled = 0
      AND EXISTS
      (
          SELECT 1
          FROM sys.trigger_events AS trigger_event
          WHERE trigger_event.object_id = trigger_row.object_id
            AND trigger_event.type_desc = N'DELETE'
      )
)
BEGIN
    SELECT
        target.SchemaName,
        target.TableName,
        trigger_row.name AS DeleteTrigger
    FROM sys.triggers AS trigger_row
    JOIN #Targets AS target ON target.ObjectId = trigger_row.parent_id
    WHERE trigger_row.is_disabled = 0
      AND EXISTS
      (
          SELECT 1
          FROM sys.trigger_events AS trigger_event
          WHERE trigger_event.object_id = trigger_row.object_id
            AND trigger_event.type_desc = N'DELETE'
      )
    ORDER BY target.SchemaName, target.TableName, trigger_row.name;

    THROW 51112, 'A selected table has an enabled DELETE trigger. Review it explicitly before cleanup.', 1;
END;

-- A foreign key from a non-target child into a target parent means deleting the target
-- would either fail or cascade into a table the operator did not select. Both outcomes
-- violate the explicit allowlist, so stop.
IF EXISTS
(
    SELECT 1
    FROM sys.foreign_keys AS fk
    JOIN #Targets AS parent_target
      ON parent_target.ObjectId = fk.referenced_object_id
    LEFT JOIN #Targets AS child_target
      ON child_target.ObjectId = fk.parent_object_id
    WHERE fk.is_disabled = 0
      AND child_target.ObjectId IS NULL
)
BEGIN
    SELECT
        child_schema.name AS ReferencingSchema,
        child_table.name AS ReferencingTable,
        fk.name AS ForeignKey,
        parent_target.SchemaName AS TargetSchema,
        parent_target.TableName AS TargetTable,
        fk.delete_referential_action_desc AS DeleteAction
    FROM sys.foreign_keys AS fk
    JOIN #Targets AS parent_target
      ON parent_target.ObjectId = fk.referenced_object_id
    JOIN sys.tables AS child_table
      ON child_table.object_id = fk.parent_object_id
    JOIN sys.schemas AS child_schema
      ON child_schema.schema_id = child_table.schema_id
    LEFT JOIN #Targets AS child_target
      ON child_target.ObjectId = fk.parent_object_id
    WHERE fk.is_disabled = 0
      AND child_target.ObjectId IS NULL
    ORDER BY
        parent_target.SchemaName,
        parent_target.TableName,
        child_schema.name,
        child_table.name,
        fk.name;

    THROW 51113, 'A non-selected table references a selected table. Add the child table intentionally or use a purpose-built cleanup.', 1;
END;

-- Determine a child-before-parent delete order using the current SQL Server FK graph.
CREATE TABLE #Remaining
(
    ObjectId int NOT NULL PRIMARY KEY,
    SchemaName sysname NOT NULL,
    TableName sysname NOT NULL
);

INSERT INTO #Remaining (ObjectId, SchemaName, TableName)
SELECT ObjectId, SchemaName, TableName
FROM #Targets;

CREATE TABLE #Ready
(
    ObjectId int NOT NULL PRIMARY KEY
);

CREATE TABLE #DeleteOrder
(
    DeleteBatch int NOT NULL,
    ObjectId int NOT NULL PRIMARY KEY,
    SchemaName sysname NOT NULL,
    TableName sysname NOT NULL
);

DECLARE @DeleteBatch int = 0;

WHILE EXISTS (SELECT 1 FROM #Remaining)
BEGIN
    DELETE FROM #Ready;

    INSERT INTO #Ready (ObjectId)
    SELECT remaining.ObjectId
    FROM #Remaining AS remaining
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys AS fk
        JOIN #Remaining AS child_remaining
          ON child_remaining.ObjectId = fk.parent_object_id
        WHERE fk.is_disabled = 0
          AND fk.referenced_object_id = remaining.ObjectId
          AND fk.parent_object_id <> fk.referenced_object_id
    );

    IF NOT EXISTS (SELECT 1 FROM #Ready)
    BEGIN
        SELECT
            child_schema.name AS ChildSchema,
            child_table.name AS ChildTable,
            fk.name AS ForeignKey,
            parent_schema.name AS ParentSchema,
            parent_table.name AS ParentTable
        FROM sys.foreign_keys AS fk
        JOIN #Remaining AS child_remaining
          ON child_remaining.ObjectId = fk.parent_object_id
        JOIN #Remaining AS parent_remaining
          ON parent_remaining.ObjectId = fk.referenced_object_id
        JOIN sys.tables AS child_table ON child_table.object_id = fk.parent_object_id
        JOIN sys.schemas AS child_schema ON child_schema.schema_id = child_table.schema_id
        JOIN sys.tables AS parent_table ON parent_table.object_id = fk.referenced_object_id
        JOIN sys.schemas AS parent_schema ON parent_schema.schema_id = parent_table.schema_id
        WHERE fk.is_disabled = 0
          AND fk.parent_object_id <> fk.referenced_object_id
        ORDER BY ChildSchema, ChildTable, ParentSchema, ParentTable, fk.name;

        THROW 51114, 'Selected tables contain a foreign-key cycle that requires a purpose-built cleanup.', 1;
    END;

    SET @DeleteBatch += 1;

    INSERT INTO #DeleteOrder (DeleteBatch, ObjectId, SchemaName, TableName)
    SELECT
        @DeleteBatch,
        remaining.ObjectId,
        remaining.SchemaName,
        remaining.TableName
    FROM #Remaining AS remaining
    JOIN #Ready AS ready ON ready.ObjectId = remaining.ObjectId;

    DELETE remaining
    FROM #Remaining AS remaining
    JOIN #Ready AS ready ON ready.ObjectId = remaining.ObjectId;
END;

CREATE TABLE #PreviewCounts
(
    ObjectId int NOT NULL PRIMARY KEY,
    SchemaName sysname NOT NULL,
    TableName sysname NOT NULL,
    RowCount bigint NOT NULL
);

DECLARE
    @ObjectId int,
    @SchemaName sysname,
    @TableName sysname,
    @Sql nvarchar(max);

DECLARE preview_cursor CURSOR LOCAL FAST_FORWARD FOR
SELECT ObjectId, SchemaName, TableName
FROM #Targets
ORDER BY SchemaName, TableName;

OPEN preview_cursor;
FETCH NEXT FROM preview_cursor INTO @ObjectId, @SchemaName, @TableName;

WHILE @@FETCH_STATUS = 0
BEGIN
    SET @Sql =
        N'INSERT INTO #PreviewCounts (ObjectId, SchemaName, TableName, RowCount) ' +
        N'SELECT ' + CONVERT(nvarchar(20), @ObjectId) + N', N''' +
        REPLACE(@SchemaName, N'''', N'''''') + N''', N''' +
        REPLACE(@TableName, N'''', N'''''') + N''', COUNT_BIG(*) ' +
        N'FROM ' + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName) + N';';

    EXEC sys.sp_executesql @Sql;

    FETCH NEXT FROM preview_cursor INTO @ObjectId, @SchemaName, @TableName;
END;

CLOSE preview_cursor;
DEALLOCATE preview_cursor;

DECLARE @PreviewMaterial nvarchar(max) =
(
    SELECT STRING_AGG(
        CONVERT(nvarchar(max),
            QUOTENAME(SchemaName) + N'.' + QUOTENAME(TableName) + N'=' + CONVERT(nvarchar(30), RowCount)
        ),
        N';'
    ) WITHIN GROUP (ORDER BY SchemaName, TableName)
    FROM #PreviewCounts
);

DECLARE @PreviewSignature varchar(64) =
    CONVERT(varchar(64), HASHBYTES('SHA2_256', @PreviewMaterial), 2);

SELECT
    counts.SchemaName,
    counts.TableName,
    counts.RowCount,
    delete_order.DeleteBatch
FROM #PreviewCounts AS counts
JOIN #DeleteOrder AS delete_order ON delete_order.ObjectId = counts.ObjectId
ORDER BY delete_order.DeleteBatch, counts.SchemaName, counts.TableName;

SELECT
    DB_NAME() AS DatabaseName,
    CAST(@Execute AS int) AS ExecuteMode,
    (SELECT COUNT(*) FROM #Targets) AS SelectedTables,
    COALESCE((SELECT SUM(RowCount) FROM #PreviewCounts), 0) AS TotalRows,
    @PreviewSignature AS CountSignature;

IF @Execute = 0
BEGIN
    PRINT 'DRY RUN ONLY: no rows were changed. Copy CountSignature into the Execute=1 command only after reviewing this exact table set and counts.';
    RETURN;
END;

IF UPPER(@ExpectedCountSignature) <> @PreviewSignature
    THROW 51115, 'CountSignature does not match the current pre-lock preview. Run a new dry run and review it before executing.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Recount with exclusive table locks held until commit/rollback. This prevents
    -- concurrent writes to selected tables after the approval signature is checked.
    CREATE TABLE #LockedCounts
    (
        ObjectId int NOT NULL PRIMARY KEY,
        SchemaName sysname NOT NULL,
        TableName sysname NOT NULL,
        RowCount bigint NOT NULL
    );

    DECLARE lock_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT ObjectId, SchemaName, TableName
    FROM #Targets
    ORDER BY SchemaName, TableName;

    OPEN lock_cursor;
    FETCH NEXT FROM lock_cursor INTO @ObjectId, @SchemaName, @TableName;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @Sql =
            N'INSERT INTO #LockedCounts (ObjectId, SchemaName, TableName, RowCount) ' +
            N'SELECT ' + CONVERT(nvarchar(20), @ObjectId) + N', N''' +
            REPLACE(@SchemaName, N'''', N'''''') + N''', N''' +
            REPLACE(@TableName, N'''', N'''''') + N''', COUNT_BIG(*) ' +
            N'FROM ' + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName) +
            N' WITH (TABLOCKX, HOLDLOCK);';

        EXEC sys.sp_executesql @Sql;

        FETCH NEXT FROM lock_cursor INTO @ObjectId, @SchemaName, @TableName;
    END;

    CLOSE lock_cursor;
    DEALLOCATE lock_cursor;

    DECLARE @LockedMaterial nvarchar(max) =
    (
        SELECT STRING_AGG(
            CONVERT(nvarchar(max),
                QUOTENAME(SchemaName) + N'.' + QUOTENAME(TableName) + N'=' + CONVERT(nvarchar(30), RowCount)
            ),
            N';'
        ) WITHIN GROUP (ORDER BY SchemaName, TableName)
        FROM #LockedCounts
    );

    DECLARE @LockedSignature varchar(64) =
        CONVERT(varchar(64), HASHBYTES('SHA2_256', @LockedMaterial), 2);

    IF @LockedSignature <> UPPER(@ExpectedCountSignature)
        THROW 51116, 'Selected-table counts changed while acquiring locks. Transaction will be rolled back; run a new dry run.', 1;

    CREATE TABLE #DeletedRows
    (
        ObjectId int NOT NULL PRIMARY KEY,
        SchemaName sysname NOT NULL,
        TableName sysname NOT NULL,
        DeletedRows bigint NOT NULL
    );

    DECLARE @DeletedRows bigint;

    DECLARE delete_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT ObjectId, SchemaName, TableName
    FROM #DeleteOrder
    ORDER BY DeleteBatch, SchemaName, TableName;

    OPEN delete_cursor;
    FETCH NEXT FROM delete_cursor INTO @ObjectId, @SchemaName, @TableName;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @DeletedRows = 0;
        SET @Sql =
            N'DELETE FROM ' + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName) + N'; ' +
            N'SET @Rows = @@ROWCOUNT;';

        EXEC sys.sp_executesql
            @Sql,
            N'@Rows bigint OUTPUT',
            @Rows = @DeletedRows OUTPUT;

        INSERT INTO #DeletedRows (ObjectId, SchemaName, TableName, DeletedRows)
        VALUES (@ObjectId, @SchemaName, @TableName, @DeletedRows);

        FETCH NEXT FROM delete_cursor INTO @ObjectId, @SchemaName, @TableName;
    END;

    CLOSE delete_cursor;
    DEALLOCATE delete_cursor;

    CREATE TABLE #PostCounts
    (
        ObjectId int NOT NULL PRIMARY KEY,
        SchemaName sysname NOT NULL,
        TableName sysname NOT NULL,
        RowCount bigint NOT NULL
    );

    DECLARE post_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT ObjectId, SchemaName, TableName
    FROM #Targets
    ORDER BY SchemaName, TableName;

    OPEN post_cursor;
    FETCH NEXT FROM post_cursor INTO @ObjectId, @SchemaName, @TableName;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @Sql =
            N'INSERT INTO #PostCounts (ObjectId, SchemaName, TableName, RowCount) ' +
            N'SELECT ' + CONVERT(nvarchar(20), @ObjectId) + N', N''' +
            REPLACE(@SchemaName, N'''', N'''''') + N''', N''' +
            REPLACE(@TableName, N'''', N'''''') + N''', COUNT_BIG(*) ' +
            N'FROM ' + QUOTENAME(@SchemaName) + N'.' + QUOTENAME(@TableName) + N';';

        EXEC sys.sp_executesql @Sql;

        FETCH NEXT FROM post_cursor INTO @ObjectId, @SchemaName, @TableName;
    END;

    CLOSE post_cursor;
    DEALLOCATE post_cursor;

    IF EXISTS (SELECT 1 FROM #PostCounts WHERE RowCount <> 0)
    BEGIN
        SELECT SchemaName, TableName, RowCount
        FROM #PostCounts
        WHERE RowCount <> 0
        ORDER BY SchemaName, TableName;

        THROW 51117, 'Post-check failed: one or more selected tables still contain rows. Transaction will be rolled back.', 1;
    END;

    COMMIT TRANSACTION;

    SELECT
        deleted.SchemaName,
        deleted.TableName,
        deleted.DeletedRows,
        delete_order.DeleteBatch
    FROM #DeletedRows AS deleted
    JOIN #DeleteOrder AS delete_order ON delete_order.ObjectId = deleted.ObjectId
    ORDER BY delete_order.DeleteBatch, deleted.SchemaName, deleted.TableName;

    SELECT
        DB_NAME() AS DatabaseName,
        CAST(1 AS int) AS ExecuteMode,
        (SELECT COUNT(*) FROM #Targets) AS ClearedTables,
        COALESCE((SELECT SUM(DeletedRows) FROM #DeletedRows), 0) AS DeletedRowsTotal,
        UPPER(@ExpectedCountSignature) AS ApprovedCountSignature;
END TRY
BEGIN CATCH
    IF CURSOR_STATUS('local', 'lock_cursor') >= 0
        CLOSE lock_cursor;
    IF CURSOR_STATUS('local', 'lock_cursor') > -3
        DEALLOCATE lock_cursor;

    IF CURSOR_STATUS('local', 'delete_cursor') >= 0
        CLOSE delete_cursor;
    IF CURSOR_STATUS('local', 'delete_cursor') > -3
        DEALLOCATE delete_cursor;

    IF CURSOR_STATUS('local', 'post_cursor') >= 0
        CLOSE post_cursor;
    IF CURSOR_STATUS('local', 'post_cursor') > -3
        DEALLOCATE post_cursor;

    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
