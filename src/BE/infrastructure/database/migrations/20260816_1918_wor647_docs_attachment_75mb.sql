SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.KnowledgeDocumentAttachments', N'U') IS NULL
BEGIN
    THROW 51001, 'KnowledgeDocumentAttachments must exist before applying WOR-647.', 1;
END;

IF EXISTS
(
    SELECT 1
    FROM dbo.KnowledgeDocumentAttachments
    WHERE SizeBytes <= 0 OR SizeBytes > 78643200
)
BEGIN
    THROW 51002, 'KnowledgeDocumentAttachments contains rows outside the 75 MB attachment policy.', 1;
END;

IF OBJECT_ID(N'dbo.CK_KnowledgeDocumentAttachments_Size', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.KnowledgeDocumentAttachments
        DROP CONSTRAINT CK_KnowledgeDocumentAttachments_Size;
END;

ALTER TABLE dbo.KnowledgeDocumentAttachments WITH CHECK
    ADD CONSTRAINT CK_KnowledgeDocumentAttachments_Size
        CHECK (SizeBytes > 0 AND SizeBytes <= 78643200);

ALTER TABLE dbo.KnowledgeDocumentAttachments
    CHECK CONSTRAINT CK_KnowledgeDocumentAttachments_Size;

COMMIT TRANSACTION;
