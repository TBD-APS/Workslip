SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
    THROW 51412, 'WOR-412 requires dbo.Users.', 1;

IF OBJECT_ID(N'dbo.InviteTokens', N'U') IS NULL
    THROW 51413, 'WOR-412 requires dbo.InviteTokens.', 1;

IF COL_LENGTH(N'dbo.Users', N'UserKind') IS NULL
BEGIN
    ALTER TABLE dbo.Users
        ADD UserKind nvarchar(32) NOT NULL
            CONSTRAINT DF_Users_UserKind DEFAULT N'Member';
END;

IF COL_LENGTH(N'dbo.InviteTokens', N'UserKind') IS NULL
BEGIN
    ALTER TABLE dbo.InviteTokens
        ADD UserKind nvarchar(32) NOT NULL
            CONSTRAINT DF_InviteTokens_UserKind DEFAULT N'Member';
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.Users')
      AND name = N'CK_Users_UserKind')
BEGIN
    EXEC(N'ALTER TABLE dbo.Users WITH CHECK
        ADD CONSTRAINT CK_Users_UserKind
        CHECK (UserKind IN (N''Member'', N''InternalTest''));');
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.InviteTokens')
      AND name = N'CK_InviteTokens_UserKind')
BEGIN
    EXEC(N'ALTER TABLE dbo.InviteTokens WITH CHECK
        ADD CONSTRAINT CK_InviteTokens_UserKind
        CHECK (UserKind IN (N''Member'', N''InternalTest''));');
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Users')
      AND name = N'IX_Users_Organization_UserKind')
BEGIN
    EXEC(N'CREATE INDEX IX_Users_Organization_UserKind
        ON dbo.Users (OrganizationId, UserKind);');
END;
