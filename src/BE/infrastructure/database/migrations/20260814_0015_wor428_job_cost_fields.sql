SET XACT_ABORT ON;
BEGIN TRANSACTION;

ALTER TABLE dbo.Users ADD BillableHourlyRate decimal(18,2) NULL;
ALTER TABLE dbo.Worksheets ADD BillableHourlyRateSnapshot decimal(18,2) NULL;

COMMIT TRANSACTION;
