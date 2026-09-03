SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.AccountingCustomerLinks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AccountingCustomerLinks
    (
        OrganizationId uniqueidentifier NOT NULL,
        CustomerId uniqueidentifier NOT NULL,
        ProviderId nvarchar(32) NOT NULL,
        ExternalCustomerNumber nvarchar(64) NOT NULL,
        LastSyncedAt datetimeoffset NOT NULL CONSTRAINT DF_AccountingCustomerLinks_LastSyncedAt DEFAULT sysutcdatetime(),
        CONSTRAINT PK_AccountingCustomerLinks PRIMARY KEY (OrganizationId, CustomerId, ProviderId),
        CONSTRAINT UQ_AccountingCustomerLinks_External UNIQUE (OrganizationId, ProviderId, ExternalCustomerNumber),
        CONSTRAINT FK_AccountingCustomerLinks_Organizations FOREIGN KEY (OrganizationId) REFERENCES dbo.Organizations(Id),
        CONSTRAINT FK_AccountingCustomerLinks_Customers FOREIGN KEY (CustomerId) REFERENCES dbo.Customers(Id),
        CONSTRAINT CK_AccountingCustomerLinks_Provider_NotBlank CHECK (LEN(LTRIM(RTRIM(ProviderId))) > 0),
        CONSTRAINT CK_AccountingCustomerLinks_External_NotBlank CHECK (LEN(LTRIM(RTRIM(ExternalCustomerNumber))) > 0)
    );
END;

IF OBJECT_ID(N'dbo.JobAccountingLinks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.JobAccountingLinks
    (
        OrganizationId uniqueidentifier NOT NULL,
        JobId uniqueidentifier NOT NULL,
        ProviderId nvarchar(32) NOT NULL,
        DraftInvoiceNumber int NULL,
        BookedInvoiceNumber int NULL,
        ExternalReference nvarchar(100) NOT NULL,
        Status nvarchar(32) NOT NULL,
        ExternalUrl nvarchar(500) NULL,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_JobAccountingLinks_CreatedAt DEFAULT sysutcdatetime(),
        LastSyncedAt datetimeoffset NOT NULL CONSTRAINT DF_JobAccountingLinks_LastSyncedAt DEFAULT sysutcdatetime(),
        CONSTRAINT PK_JobAccountingLinks PRIMARY KEY (OrganizationId, JobId, ProviderId),
        CONSTRAINT UQ_JobAccountingLinks_Reference UNIQUE (OrganizationId, ProviderId, ExternalReference),
        CONSTRAINT FK_JobAccountingLinks_Organizations FOREIGN KEY (OrganizationId) REFERENCES dbo.Organizations(Id),
        CONSTRAINT FK_JobAccountingLinks_Jobs FOREIGN KEY (JobId) REFERENCES dbo.JobReports(Id),
        CONSTRAINT CK_JobAccountingLinks_Status CHECK (Status IN (N'Draft', N'Booked', N'Paid', N'Overdue', N'Unknown'))
    );
END;

IF OBJECT_ID(N'dbo.JobBillableItems', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.JobBillableItems
    (
        Id uniqueidentifier NOT NULL,
        OrganizationId uniqueidentifier NOT NULL,
        JobId uniqueidentifier NOT NULL,
        Kind nvarchar(16) NOT NULL,
        Description nvarchar(250) NOT NULL,
        Quantity decimal(18,3) NOT NULL,
        UnitNetPrice decimal(18,2) NOT NULL,
        Source nvarchar(32) NOT NULL CONSTRAINT DF_JobBillableItems_Source DEFAULT N'manual',
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_JobBillableItems_CreatedAt DEFAULT sysutcdatetime(),
        UpdatedAt datetimeoffset NOT NULL CONSTRAINT DF_JobBillableItems_UpdatedAt DEFAULT sysutcdatetime(),
        CONSTRAINT PK_JobBillableItems PRIMARY KEY (Id),
        CONSTRAINT FK_JobBillableItems_Organizations FOREIGN KEY (OrganizationId) REFERENCES dbo.Organizations(Id),
        CONSTRAINT FK_JobBillableItems_Jobs FOREIGN KEY (JobId) REFERENCES dbo.JobReports(Id),
        CONSTRAINT CK_JobBillableItems_Kind CHECK (Kind IN (N'material', N'outlay')),
        CONSTRAINT CK_JobBillableItems_Description_NotBlank CHECK (LEN(LTRIM(RTRIM(Description))) > 0),
        CONSTRAINT CK_JobBillableItems_Quantity_Positive CHECK (Quantity > 0),
        CONSTRAINT CK_JobBillableItems_UnitNetPrice_NonNegative CHECK (UnitNetPrice >= 0)
    );

    CREATE INDEX IX_JobBillableItems_Organization_Job
        ON dbo.JobBillableItems (OrganizationId, JobId, Kind, CreatedAt);
END;

IF OBJECT_ID(N'dbo.JobAccountingDocumentLinks', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.JobAccountingDocumentLinks
    (
        OrganizationId uniqueidentifier NOT NULL,
        JobId uniqueidentifier NOT NULL,
        ProviderId nvarchar(32) NOT NULL,
        ExternalDocumentId nvarchar(128) NOT NULL,
        DocumentNumber nvarchar(128) NOT NULL,
        DocumentType nvarchar(32) NOT NULL,
        Amount decimal(18,2) NOT NULL,
        DocumentDate date NOT NULL,
        Status nvarchar(32) NOT NULL,
        ExternalUrl nvarchar(500) NULL,
        LinkedAt datetimeoffset NOT NULL CONSTRAINT DF_JobAccountingDocumentLinks_LinkedAt DEFAULT sysutcdatetime(),
        CONSTRAINT PK_JobAccountingDocumentLinks PRIMARY KEY (OrganizationId, JobId, ProviderId, ExternalDocumentId),
        CONSTRAINT FK_JobAccountingDocumentLinks_Organizations FOREIGN KEY (OrganizationId) REFERENCES dbo.Organizations(Id),
        CONSTRAINT FK_JobAccountingDocumentLinks_Jobs FOREIGN KEY (JobId) REFERENCES dbo.JobReports(Id)
    );

    CREATE INDEX IX_JobAccountingDocumentLinks_Organization_Job
        ON dbo.JobAccountingDocumentLinks (OrganizationId, JobId, LinkedAt DESC);
END;
