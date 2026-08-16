SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.JobConversationMessages', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.JobConversationMessages
    (
        Id uniqueidentifier NOT NULL,
        OrganizationId uniqueidentifier NOT NULL,
        JobId uniqueidentifier NOT NULL,
        AuthorUserId uniqueidentifier NOT NULL,
        Body nvarchar(4000) NOT NULL,
        MentionedUserIdsJson nvarchar(max) NOT NULL CONSTRAINT DF_JobConversationMessages_MentionedUserIdsJson DEFAULT N'[]',
        ActionType nvarchar(40) NULL,
        ActionTargetUserId uniqueidentifier NULL,
        ActionStatus nvarchar(40) NULL,
        ActionResolvedByUserId uniqueidentifier NULL,
        ActionResolvedUtc datetimeoffset NULL,
        CreatedUtc datetimeoffset NOT NULL CONSTRAINT DF_JobConversationMessages_CreatedUtc DEFAULT sysutcdatetime(),
        CONSTRAINT PK_JobConversationMessages PRIMARY KEY (Id),
        CONSTRAINT CK_JobConversationMessages_MentionsJson CHECK (isjson(MentionedUserIdsJson) = 1),
        CONSTRAINT CK_JobConversationMessages_ActionType CHECK (ActionType IS NULL OR ActionType IN (N'Acknowledge', N'SubmitForReview')),
        CONSTRAINT CK_JobConversationMessages_ActionStatus CHECK (ActionStatus IS NULL OR ActionStatus IN (N'Pending', N'Completed')),
        CONSTRAINT CK_JobConversationMessages_ActionShape CHECK
        (
            (ActionType IS NULL AND ActionTargetUserId IS NULL AND ActionStatus IS NULL AND ActionResolvedByUserId IS NULL AND ActionResolvedUtc IS NULL)
            OR
            (ActionType IS NOT NULL AND ActionTargetUserId IS NOT NULL AND ActionStatus IS NOT NULL)
        ),
        CONSTRAINT CK_JobConversationMessages_ActionResolution CHECK
        (
            (ActionStatus IS NULL)
            OR (ActionStatus = N'Pending' AND ActionResolvedByUserId IS NULL AND ActionResolvedUtc IS NULL)
            OR (ActionStatus = N'Completed' AND ActionResolvedByUserId IS NOT NULL AND ActionResolvedUtc IS NOT NULL)
        ),
        CONSTRAINT FK_JobConversationMessages_JobReports FOREIGN KEY (OrganizationId, JobId)
            REFERENCES dbo.JobReports (OrganizationId, Id) ON DELETE CASCADE,
        CONSTRAINT FK_JobConversationMessages_Author FOREIGN KEY (OrganizationId, AuthorUserId)
            REFERENCES dbo.Users (OrganizationId, Id),
        CONSTRAINT FK_JobConversationMessages_ActionTarget FOREIGN KEY (OrganizationId, ActionTargetUserId)
            REFERENCES dbo.Users (OrganizationId, Id),
        CONSTRAINT FK_JobConversationMessages_ActionResolver FOREIGN KEY (OrganizationId, ActionResolvedByUserId)
            REFERENCES dbo.Users (OrganizationId, Id)
    );

    CREATE INDEX IX_JobConversationMessages_Job_CreatedUtc
        ON dbo.JobConversationMessages (OrganizationId, JobId, CreatedUtc DESC, Id DESC);

    CREATE INDEX IX_JobConversationMessages_ActionTarget_Status
        ON dbo.JobConversationMessages (OrganizationId, ActionTargetUserId, ActionStatus)
        WHERE ActionTargetUserId IS NOT NULL;
END;

IF OBJECT_ID(N'dbo.JobConversationReads', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.JobConversationReads
    (
        OrganizationId uniqueidentifier NOT NULL,
        JobId uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        LastReadUtc datetimeoffset NOT NULL,
        CONSTRAINT PK_JobConversationReads PRIMARY KEY (OrganizationId, JobId, UserId),
        CONSTRAINT FK_JobConversationReads_JobReports FOREIGN KEY (OrganizationId, JobId)
            REFERENCES dbo.JobReports (OrganizationId, Id) ON DELETE CASCADE,
        CONSTRAINT FK_JobConversationReads_Users FOREIGN KEY (OrganizationId, UserId)
            REFERENCES dbo.Users (OrganizationId, Id)
    );
END;

COMMIT TRANSACTION;
