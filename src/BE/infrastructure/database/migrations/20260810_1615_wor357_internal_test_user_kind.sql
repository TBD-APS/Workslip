SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
    THROW 51000, 'WOR-357 requires dbo.Users.', 1;

IF COL_LENGTH(N'dbo.Users', N'UserKind') IS NULL
BEGIN
    ALTER TABLE dbo.Users
        ADD UserKind nvarchar(32) NOT NULL
            CONSTRAINT DF_Users_UserKind DEFAULT N'Member';
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.Users')
      AND name = N'CK_Users_UserKind')
BEGIN
    ALTER TABLE dbo.Users WITH CHECK
        ADD CONSTRAINT CK_Users_UserKind
        CHECK (UserKind IN (N'Member', N'InternalTest'));
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.Users')
      AND name = N'IX_Users_Organization_UserKind')
BEGIN
    CREATE INDEX IX_Users_Organization_UserKind
        ON dbo.Users (OrganizationId, UserKind);
END;
