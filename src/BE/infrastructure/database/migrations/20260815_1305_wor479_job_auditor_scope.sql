SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

IF COL_LENGTH('dbo.JobReports', 'IsInAuditorScope') IS NULL
BEGIN
    ALTER TABLE dbo.JobReports
        ADD IsInAuditorScope bit NOT NULL
            CONSTRAINT DF_JobReports_IsInAuditorScope DEFAULT (1);
END;

IF COL_LENGTH('dbo.JobReports', 'AuditorScopeReason') IS NULL
BEGIN
    ALTER TABLE dbo.JobReports
        ADD AuditorScopeReason nvarchar(500) NULL;
END;

COMMIT TRANSACTION;
