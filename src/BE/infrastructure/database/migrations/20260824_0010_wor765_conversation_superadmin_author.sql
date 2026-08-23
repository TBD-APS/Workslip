SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Conversation messages are tenant-scoped by (OrganizationId, JobId), but the actor
-- may be a Superadmin using a delegated organization session. In that case the
-- authenticated actor remains a user in the home organization while the effective
-- OrganizationId points at the customer tenant. Users.Id is the global primary key,
-- so the author relation must follow the global actor identity rather than require
-- the actor to also exist inside the effective tenant.
IF OBJECT_ID(N'dbo.JobConversationMessages', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.FK_JobConversationMessages_Author', N'F') IS NOT NULL
    BEGIN
        ALTER TABLE dbo.JobConversationMessages
            DROP CONSTRAINT FK_JobConversationMessages_Author;
    END;

    ALTER TABLE dbo.JobConversationMessages WITH CHECK
        ADD CONSTRAINT FK_JobConversationMessages_Author
            FOREIGN KEY (AuthorUserId)
            REFERENCES dbo.Users (Id);

    ALTER TABLE dbo.JobConversationMessages
        CHECK CONSTRAINT FK_JobConversationMessages_Author;
END;

COMMIT TRANSACTION;
