SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

IF OBJECT_ID('dbo.OrganizationAccountingSettings', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.OrganizationAccountingSettings
    (
        OrganizationId uniqueidentifier NOT NULL,
        ProviderId nvarchar(80) NOT NULL,
        UpdatedAt datetimeoffset NOT NULL,
        CONSTRAINT PK_OrganizationAccountingSettings PRIMARY KEY (OrganizationId),
        CONSTRAINT FK_OrganizationAccountingSettings_Organizations
            FOREIGN KEY (OrganizationId) REFERENCES dbo.Organizations(Id) ON DELETE CASCADE
    );
END;

COMMIT TRANSACTION;
