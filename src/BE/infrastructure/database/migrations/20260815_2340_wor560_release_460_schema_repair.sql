SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

-- Repair databases that were created by the pre-WOR-510 local EF bootstrap and
-- therefore may have post-baseline migrations recorded as applied even though
-- their SQL effects were never established. Every operation below is forward-only
-- and safe when the intended 4.6 schema is already present.

IF OBJECT_ID(N'dbo.UserBillingRates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserBillingRates
    (
        OrganizationId uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        BillableHourlyRate decimal(18,2) NULL,
        UpdatedAt datetimeoffset NOT NULL CONSTRAINT DF_UserBillingRates_UpdatedAt DEFAULT sysutcdatetime(),
        CONSTRAINT PK_UserBillingRates PRIMARY KEY (OrganizationId, UserId),
        CONSTRAINT FK_UserBillingRates_Users FOREIGN KEY (OrganizationId, UserId)
            REFERENCES dbo.Users (OrganizationId, Id),
        CONSTRAINT CK_UserBillingRates_Rate CHECK (
            BillableHourlyRate IS NULL OR (BillableHourlyRate >= 0 AND BillableHourlyRate <= 100000)
        )
    );
END;

IF OBJECT_ID(N'dbo.WorksheetBillingSnapshots', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.WorksheetBillingSnapshots
    (
        OrganizationId uniqueidentifier NOT NULL,
        WorksheetId uniqueidentifier NOT NULL,
        BillableHourlyRateSnapshot decimal(18,2) NULL,
        CapturedAtUtc datetimeoffset NOT NULL,
        CONSTRAINT PK_WorksheetBillingSnapshots PRIMARY KEY (OrganizationId, WorksheetId),
        CONSTRAINT FK_WorksheetBillingSnapshots_Worksheets FOREIGN KEY (OrganizationId, WorksheetId)
            REFERENCES dbo.Worksheets (OrganizationId, Id),
        CONSTRAINT CK_WorksheetBillingSnapshots_Rate CHECK (
            BillableHourlyRateSnapshot IS NULL OR (BillableHourlyRateSnapshot >= 0 AND BillableHourlyRateSnapshot <= 100000)
        )
    );
END;
ELSE
BEGIN
    -- A database can have WOR-428's first migration physically applied while the
    -- follow-up tenant FK migration was incorrectly baseline-marked. Reconcile the
    -- named FK only when it is not already the intended two-column relationship.
    IF NOT EXISTS
    (
        SELECT 1
        FROM sys.foreign_keys fk
        WHERE fk.parent_object_id = OBJECT_ID(N'dbo.WorksheetBillingSnapshots')
          AND fk.referenced_object_id = OBJECT_ID(N'dbo.Worksheets')
          AND fk.name = N'FK_WorksheetBillingSnapshots_Worksheets'
          AND 2 =
          (
              SELECT COUNT(*)
              FROM sys.foreign_key_columns fkc
              WHERE fkc.constraint_object_id = fk.object_id
          )
          AND EXISTS
          (
              SELECT 1
              FROM sys.foreign_key_columns fkc
              INNER JOIN sys.columns parentColumn
                  ON parentColumn.object_id = fkc.parent_object_id
                 AND parentColumn.column_id = fkc.parent_column_id
              INNER JOIN sys.columns referencedColumn
                  ON referencedColumn.object_id = fkc.referenced_object_id
                 AND referencedColumn.column_id = fkc.referenced_column_id
              WHERE fkc.constraint_object_id = fk.object_id
                AND parentColumn.name = N'OrganizationId'
                AND referencedColumn.name = N'OrganizationId'
          )
          AND EXISTS
          (
              SELECT 1
              FROM sys.foreign_key_columns fkc
              INNER JOIN sys.columns parentColumn
                  ON parentColumn.object_id = fkc.parent_object_id
                 AND parentColumn.column_id = fkc.parent_column_id
              INNER JOIN sys.columns referencedColumn
                  ON referencedColumn.object_id = fkc.referenced_object_id
                 AND referencedColumn.column_id = fkc.referenced_column_id
              WHERE fkc.constraint_object_id = fk.object_id
                AND parentColumn.name = N'WorksheetId'
                AND referencedColumn.name = N'Id'
          )
    )
    BEGIN
        IF OBJECT_ID(N'dbo.FK_WorksheetBillingSnapshots_Worksheets', N'F') IS NOT NULL
        BEGIN
            ALTER TABLE dbo.WorksheetBillingSnapshots
                DROP CONSTRAINT FK_WorksheetBillingSnapshots_Worksheets;
        END;

        ALTER TABLE dbo.WorksheetBillingSnapshots WITH CHECK
            ADD CONSTRAINT FK_WorksheetBillingSnapshots_Worksheets
            FOREIGN KEY (OrganizationId, WorksheetId)
            REFERENCES dbo.Worksheets (OrganizationId, Id);
    END;
END;

IF OBJECT_ID(N'dbo.KnowledgeDocuments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.KnowledgeDocuments
    (
        Id uniqueidentifier NOT NULL,
        OrganizationId uniqueidentifier NOT NULL,
        Title nvarchar(200) NOT NULL,
        Content nvarchar(max) NOT NULL CONSTRAINT DF_KnowledgeDocuments_Content DEFAULT N'',
        TagsJson nvarchar(2000) NOT NULL CONSTRAINT DF_KnowledgeDocuments_TagsJson DEFAULT N'[]',
        CreatedByUserId uniqueidentifier NULL,
        UpdatedByUserId uniqueidentifier NULL,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_KnowledgeDocuments_CreatedAt DEFAULT sysutcdatetime(),
        UpdatedAt datetimeoffset NOT NULL CONSTRAINT DF_KnowledgeDocuments_UpdatedAt DEFAULT sysutcdatetime(),
        Revision bigint NOT NULL CONSTRAINT DF_KnowledgeDocuments_Revision DEFAULT 1,
        CONSTRAINT PK_KnowledgeDocuments PRIMARY KEY (Id),
        CONSTRAINT UQ_KnowledgeDocuments_Organization_Id UNIQUE (OrganizationId, Id),
        CONSTRAINT FK_KnowledgeDocuments_Organizations_OrganizationId
            FOREIGN KEY (OrganizationId) REFERENCES dbo.Organizations(Id),
        CONSTRAINT CK_KnowledgeDocuments_Title_NotBlank
            CHECK (LEN(LTRIM(RTRIM(Title))) > 0),
        CONSTRAINT CK_KnowledgeDocuments_TagsJson_IsJson
            CHECK (ISJSON(TagsJson) = 1),
        CONSTRAINT CK_KnowledgeDocuments_Revision_Positive
            CHECK (Revision > 0)
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.KnowledgeDocuments')
      AND name = N'IX_KnowledgeDocuments_Organization_UpdatedAt'
)
BEGIN
    CREATE INDEX IX_KnowledgeDocuments_Organization_UpdatedAt
        ON dbo.KnowledgeDocuments (OrganizationId, UpdatedAt DESC, Id);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.KnowledgeDocuments')
      AND name = N'IX_KnowledgeDocuments_Organization_Title'
)
BEGIN
    CREATE INDEX IX_KnowledgeDocuments_Organization_Title
        ON dbo.KnowledgeDocuments (OrganizationId, Title, Id);
END;

IF OBJECT_ID(N'dbo.KnowledgeDocumentAttachments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.KnowledgeDocumentAttachments
    (
        Id uniqueidentifier NOT NULL,
        OrganizationId uniqueidentifier NOT NULL,
        DocumentId uniqueidentifier NOT NULL,
        FileName nvarchar(180) NOT NULL,
        ContentType nvarchar(100) NOT NULL,
        SizeBytes bigint NOT NULL,
        UploadedByUserId uniqueidentifier NULL,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_KnowledgeDocumentAttachments_CreatedAt DEFAULT sysutcdatetime(),
        CONSTRAINT PK_KnowledgeDocumentAttachments PRIMARY KEY (Id),
        CONSTRAINT FK_KnowledgeDocumentAttachments_Document
            FOREIGN KEY (OrganizationId, DocumentId)
            REFERENCES dbo.KnowledgeDocuments(OrganizationId, Id)
            ON DELETE CASCADE,
        CONSTRAINT CK_KnowledgeDocumentAttachments_FileName_NotBlank
            CHECK (LEN(LTRIM(RTRIM(FileName))) > 0),
        CONSTRAINT CK_KnowledgeDocumentAttachments_ContentType_NotBlank
            CHECK (LEN(LTRIM(RTRIM(ContentType))) > 0),
        CONSTRAINT CK_KnowledgeDocumentAttachments_Size
            CHECK (SizeBytes > 0 AND SizeBytes <= 20971520)
    );
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.KnowledgeDocumentAttachments')
      AND name = N'IX_KnowledgeDocumentAttachments_Organization_Document_CreatedAt'
)
BEGIN
    CREATE INDEX IX_KnowledgeDocumentAttachments_Organization_Document_CreatedAt
        ON dbo.KnowledgeDocumentAttachments (OrganizationId, DocumentId, CreatedAt, Id);
END;

IF COL_LENGTH(N'dbo.JobReports', N'IsInAuditorScope') IS NULL
BEGIN
    ALTER TABLE dbo.JobReports
        ADD IsInAuditorScope bit NOT NULL
            CONSTRAINT DF_JobReports_IsInAuditorScope DEFAULT (1);
END;

IF COL_LENGTH(N'dbo.JobReports', N'AuditorScopeReason') IS NULL
BEGIN
    ALTER TABLE dbo.JobReports
        ADD AuditorScopeReason nvarchar(500) NULL;
END;

-- Re-run the WOR-412 legacy development identity reconciliation. It is naturally
-- idempotent because it only changes exact legacy identities still marked Member.
IF OBJECT_ID(N'dbo.Users', N'U') IS NOT NULL
   AND COL_LENGTH(N'dbo.Users', N'UserKind') IS NOT NULL
   AND COL_LENGTH(N'dbo.Users', N'Email') IS NOT NULL
   AND COL_LENGTH(N'dbo.Users', N'DisplayName') IS NOT NULL
   AND COL_LENGTH(N'dbo.Users', N'Role') IS NOT NULL
   AND COL_LENGTH(N'dbo.Users', N'UpdatedAt') IS NOT NULL
BEGIN
    DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();

    UPDATE dbo.Users
    SET UserKind = N'InternalTest',
        UpdatedAt = @now
    WHERE UserKind = N'Member'
      AND
      (
            (Id = CONVERT(uniqueidentifier, 'A1A1A1A1-DA5B-4CC4-BBEB-07B40CAB806F')
             AND Email = N'admin@17v3ygzs.mailosaur.net'
             AND DisplayName = N'Niels Petersen'
             AND Role = N'Admin')
         OR (Id = CONVERT(uniqueidentifier, 'B2B2B2B2-DA5B-4CC4-BBEB-07B40CAB806F')
             AND Email = N'user@17v3ygzs.mailosaur.net'
             AND DisplayName = N'Arne Arnesen'
             AND Role = N'User')
         OR (Id = CONVERT(uniqueidentifier, 'C3C3C3C3-DA5B-4CC4-BBEB-07B40CAB806F')
             AND Email = N'auditor@17v3ygzs.mailosaur.net'
             AND DisplayName = N'Auditor Jakobsen'
             AND Role = N'Auditor')
      );
END;

COMMIT TRANSACTION;
