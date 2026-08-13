SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.FK_WorksheetBillingSnapshots_Worksheets', N'F') IS NOT NULL
BEGIN
    ALTER TABLE dbo.WorksheetBillingSnapshots
        DROP CONSTRAINT FK_WorksheetBillingSnapshots_Worksheets;
END;

ALTER TABLE dbo.WorksheetBillingSnapshots WITH CHECK
    ADD CONSTRAINT FK_WorksheetBillingSnapshots_Worksheets
    FOREIGN KEY (OrganizationId, WorksheetId)
    REFERENCES dbo.Worksheets (OrganizationId, Id);

COMMIT TRANSACTION;
