-- Add AccountingProviderId to Organizations table
ALTER TABLE dbo.Organizations
ADD AccountingProviderId nvarchar(100) NULL;
