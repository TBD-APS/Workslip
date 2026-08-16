SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH(N'dbo.JobConversationMessages', N'ActionDueUtc') IS NULL
BEGIN
    ALTER TABLE dbo.JobConversationMessages
        ADD ActionDueUtc datetimeoffset NULL;
END;

IF OBJECT_ID(N'dbo.CK_JobConversationMessages_ActionType', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.JobConversationMessages
        DROP CONSTRAINT CK_JobConversationMessages_ActionType;
END;

ALTER TABLE dbo.JobConversationMessages
    ADD CONSTRAINT CK_JobConversationMessages_ActionType CHECK
    (
        ActionType IS NULL
        OR ActionType IN (
            N'Acknowledge',
            N'SubmitForReview',
            N'CreateTask',
            N'RemindMe',
            N'AssignSelf'
        )
    );

IF OBJECT_ID(N'dbo.CK_JobConversationMessages_ActionDueUtc', N'C') IS NOT NULL
BEGIN
    ALTER TABLE dbo.JobConversationMessages
        DROP CONSTRAINT CK_JobConversationMessages_ActionDueUtc;
END;

-- ActionDueUtc may have been added earlier in this same SQL batch. Compile the
-- dependent constraint only after the ALTER TABLE above has executed so fresh
-- databases do not fail SQL Server name binding with "Invalid column name".
EXEC(N'
ALTER TABLE dbo.JobConversationMessages
    ADD CONSTRAINT CK_JobConversationMessages_ActionDueUtc CHECK
    (
        (ActionType = N''RemindMe'' AND ActionDueUtc IS NOT NULL)
        OR (ISNULL(ActionType, N'''') <> N''RemindMe'' AND ActionDueUtc IS NULL)
    );
');

COMMIT TRANSACTION;
