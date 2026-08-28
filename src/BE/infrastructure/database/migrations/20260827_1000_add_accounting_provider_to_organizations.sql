-- Add AccountingProviderId to Organizations table
IF COL_LENGTH('dbo.Organizations', 'AccountingProviderId') IS NULL
BEGIN
    ALTER TABLE dbo.Organizations
    ADD AccountingProviderId nvarchar(100) NULL;
END
