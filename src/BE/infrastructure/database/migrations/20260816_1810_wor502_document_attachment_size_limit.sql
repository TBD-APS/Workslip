SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.KnowledgeDocumentAttachments', N'U') IS NOT NULL
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM sys.check_constraints
        WHERE parent_object_id = OBJECT_ID(N'dbo.KnowledgeDocumentAttachments')
          AND name = N'CK_KnowledgeDocumentAttachments_Size'
    )
    BEGIN
        ALTER TABLE dbo.KnowledgeDocumentAttachments
            DROP CONSTRAINT CK_KnowledgeDocumentAttachments_Size;
    END;

    ALTER TABLE dbo.KnowledgeDocumentAttachments
        ADD CONSTRAINT CK_KnowledgeDocumentAttachments_Size
            CHECK (SizeBytes > 0 AND SizeBytes <= 78643200);
END;