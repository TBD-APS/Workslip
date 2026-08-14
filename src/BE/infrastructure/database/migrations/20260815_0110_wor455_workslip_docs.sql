SET XACT_ABORT ON;

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
        CONSTRAINT FK_KnowledgeDocuments_Organizations_OrganizationId
            FOREIGN KEY (OrganizationId) REFERENCES dbo.Organizations(Id),
        CONSTRAINT CK_KnowledgeDocuments_Title_NotBlank
            CHECK (LEN(LTRIM(RTRIM(Title))) > 0),
        CONSTRAINT CK_KnowledgeDocuments_TagsJson_IsJson
            CHECK (ISJSON(TagsJson) = 1),
        CONSTRAINT CK_KnowledgeDocuments_Revision_Positive
            CHECK (Revision > 0)
    );

    CREATE INDEX IX_KnowledgeDocuments_Organization_UpdatedAt
        ON dbo.KnowledgeDocuments (OrganizationId, UpdatedAt DESC, Id);

    CREATE INDEX IX_KnowledgeDocuments_Organization_Title
        ON dbo.KnowledgeDocuments (OrganizationId, Title, Id);
END;
