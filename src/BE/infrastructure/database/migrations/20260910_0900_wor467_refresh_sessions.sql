SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.RefreshSessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RefreshSessions
    (
        Id uniqueidentifier NOT NULL,
        FamilyId uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        OrganizationId uniqueidentifier NOT NULL,
        TokenHash char(64) NOT NULL,
        CreatedAt datetimeoffset NOT NULL,
        ExpiresAt datetimeoffset NOT NULL,
        UsedAt datetimeoffset NULL,
        GraceReuseAt datetimeoffset NULL,
        RevokedAt datetimeoffset NULL,
        ConcurrencyStamp uniqueidentifier NOT NULL,
        CONSTRAINT PK_RefreshSessions PRIMARY KEY (Id),
        CONSTRAINT FK_RefreshSessions_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE,
        CONSTRAINT FK_RefreshSessions_Organizations FOREIGN KEY (OrganizationId) REFERENCES dbo.Organizations(Id),
        CONSTRAINT CK_RefreshSessions_TokenHash CHECK (LEN(TokenHash) = 64)
    );

    CREATE UNIQUE INDEX UX_RefreshSessions_TokenHash ON dbo.RefreshSessions(TokenHash);
    CREATE INDEX IX_RefreshSessions_Family_Expires ON dbo.RefreshSessions(FamilyId, ExpiresAt);
    CREATE INDEX IX_RefreshSessions_Organization_User ON dbo.RefreshSessions(OrganizationId, UserId);
END;
