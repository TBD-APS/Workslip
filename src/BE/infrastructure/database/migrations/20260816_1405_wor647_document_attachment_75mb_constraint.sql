SET XACT_ABORT ON;
SET NOCOUNT ON;

BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.KnowledgeDocumentAttachments', N'U') IS NOT NULL
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM dbo.KnowledgeDocumentAttachments
        WHERE SizeBytes <= 0
           OR SizeBytes > 78643200
    )
    BEGIN
        THROW 50001, 'KnowledgeDocumentAttachments contains SizeBytes values outside the 1..78643200 contract.', 1;
    END;

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

    ALTER TABLE dbo.KnowledgeDocumentAttachments WITH CHECK
        ADD CONSTRAINT CK_KnowledgeDocumentAttachments_Size
        CHECK (SizeBytes > 0 AND SizeBytes <= 78643200);

    ALTER TABLE dbo.KnowledgeDocumentAttachments
        CHECK CONSTRAINT CK_KnowledgeDocumentAttachments_Size;
END;

COMMIT TRANSACTION;
