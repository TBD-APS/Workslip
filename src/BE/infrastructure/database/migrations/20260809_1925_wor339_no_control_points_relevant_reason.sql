-- WOR-339: the shared explanation for a job where no control points are relevant
-- belongs directly to JobReports and must not use a generic database column name.
-- Rename preserves any existing value without copying or recreating data.

SET XACT_ABORT ON;

IF COL_LENGTH(N'dbo.JobReports', N'Remarks') IS NULL
    THROW 51030, 'Expected dbo.JobReports.Remarks before WOR-339 migration.', 1;

IF COL_LENGTH(N'dbo.JobReports', N'NoControlPointsRelevantReason') IS NOT NULL
    THROW 51031, 'dbo.JobReports.NoControlPointsRelevantReason already exists before WOR-339 migration.', 1;

BEGIN TRANSACTION;

EXEC sys.sp_rename
    @objname = N'dbo.JobReports.Remarks',
    @newname = N'NoControlPointsRelevantReason',
    @objtype = N'COLUMN';

COMMIT TRANSACTION;
