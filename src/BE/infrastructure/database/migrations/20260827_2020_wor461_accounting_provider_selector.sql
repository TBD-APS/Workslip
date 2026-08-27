SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

IF COL_LENGTH('dbo.Organizations', 'AccountingProviderId') IS NULL
BEGIN
    ALTER TABLE dbo.Organizations
        ADD AccountingProviderId nvarchar(80) NULL;
END;

COMMIT TRANSACTION;
