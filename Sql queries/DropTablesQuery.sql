USE [db-npteknik-pre]
GO

DECLARE @sql NVARCHAR(MAX) = N'';

-- Drop foreign keys first
SELECT @sql += N'
ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(parent_object_id)) +
N' DROP CONSTRAINT ' + QUOTENAME(name) + N';'
FROM sys.foreign_keys;

EXEC sp_executesql @sql;

SET @sql = N'';

-- Drop all tables in this database
SELECT @sql += N'
DROP TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';'
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id;

EXEC sp_executesql @sql;