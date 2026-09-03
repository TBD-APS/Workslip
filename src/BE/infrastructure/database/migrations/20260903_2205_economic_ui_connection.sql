SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.EconomicConnections', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EconomicConnections
    (
        OrganizationId uniqueidentifier NOT NULL,
        AgreementGrantTokenCiphertext nvarchar(max) NOT NULL,
        AgreementNumber nvarchar(64) NULL,
        CompanyName nvarchar(250) NULL,
        ConnectedAt datetimeoffset NOT NULL CONSTRAINT DF_EconomicConnections_ConnectedAt DEFAULT sysutcdatetime(),
        UpdatedAt datetimeoffset NOT NULL CONSTRAINT DF_EconomicConnections_UpdatedAt DEFAULT sysutcdatetime(),
        CONSTRAINT PK_EconomicConnections PRIMARY KEY (OrganizationId),
        CONSTRAINT FK_EconomicConnections_Organizations FOREIGN KEY (OrganizationId) REFERENCES dbo.Organizations(Id),
        CONSTRAINT CK_EconomicConnections_Ciphertext_NotBlank CHECK (LEN(LTRIM(RTRIM(AgreementGrantTokenCiphertext))) > 0)
    );
END;

IF OBJECT_ID(N'dbo.EconomicConnectAttempts', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EconomicConnectAttempts
    (
        CorrelationHash char(64) NOT NULL,
        OrganizationId uniqueidentifier NOT NULL,
        ExpiresAt datetimeoffset NOT NULL,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_EconomicConnectAttempts_CreatedAt DEFAULT sysutcdatetime(),
        CONSTRAINT PK_EconomicConnectAttempts PRIMARY KEY (CorrelationHash),
        CONSTRAINT FK_EconomicConnectAttempts_Organizations FOREIGN KEY (OrganizationId) REFERENCES dbo.Organizations(Id),
        CONSTRAINT CK_EconomicConnectAttempts_HashLength CHECK (LEN(CorrelationHash) = 64)
    );

    CREATE INDEX IX_EconomicConnectAttempts_Organization_Expires
        ON dbo.EconomicConnectAttempts (OrganizationId, ExpiresAt);
END;
