SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.AuditorAssignments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditorAssignments
    (
        Id uniqueidentifier NOT NULL,
        AuditorUserId uniqueidentifier NOT NULL,
        ControlOrganizationId uniqueidentifier NOT NULL,
        TargetOrganizationId uniqueidentifier NOT NULL,
        AuthorizationArea nvarchar(80) NOT NULL,
        ActiveFrom datetimeoffset NOT NULL,
        ActiveUntil datetimeoffset NULL,
        IsActive bit NOT NULL CONSTRAINT DF_AuditorAssignments_IsActive DEFAULT (1),
        CreatedByUserId uniqueidentifier NOT NULL,
        UpdatedByUserId uniqueidentifier NOT NULL,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_AuditorAssignments_CreatedAt DEFAULT (sysutcdatetime()),
        UpdatedAt datetimeoffset NOT NULL CONSTRAINT DF_AuditorAssignments_UpdatedAt DEFAULT (sysutcdatetime()),
        CONSTRAINT PK_AuditorAssignments PRIMARY KEY (Id),
        CONSTRAINT FK_AuditorAssignments_AuditorUser FOREIGN KEY (AuditorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_AuditorAssignments_ControlOrganization FOREIGN KEY (ControlOrganizationId) REFERENCES dbo.Organizations(Id),
        CONSTRAINT FK_AuditorAssignments_TargetOrganization FOREIGN KEY (TargetOrganizationId) REFERENCES dbo.Organizations(Id),
        CONSTRAINT FK_AuditorAssignments_CreatedBy FOREIGN KEY (CreatedByUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_AuditorAssignments_UpdatedBy FOREIGN KEY (UpdatedByUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_AuditorAssignments_ActiveWindow CHECK (ActiveUntil IS NULL OR ActiveUntil > ActiveFrom),
        CONSTRAINT CK_AuditorAssignments_AuthorizationArea CHECK (LEN(LTRIM(RTRIM(AuthorizationArea))) > 0)
    );

    CREATE INDEX IX_AuditorAssignments_Auditor_Active
        ON dbo.AuditorAssignments (AuditorUserId, ControlOrganizationId, IsActive, ActiveFrom, ActiveUntil)
        INCLUDE (TargetOrganizationId, AuthorizationArea);

    CREATE INDEX IX_AuditorAssignments_TargetOrganization
        ON dbo.AuditorAssignments (TargetOrganizationId, IsActive)
        INCLUDE (AuditorUserId, AuthorizationArea, ActiveFrom, ActiveUntil);
END;

IF OBJECT_ID(N'dbo.AuditorFindings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditorFindings
    (
        Id uniqueidentifier NOT NULL,
        AssignmentId uniqueidentifier NOT NULL,
        TargetOrganizationId uniqueidentifier NOT NULL,
        JobReportId uniqueidentifier NOT NULL,
        Category nvarchar(8) NOT NULL,
        Description nvarchar(4000) NOT NULL,
        Reference nvarchar(500) NULL,
        DueAt datetimeoffset NULL,
        Status nvarchar(40) NOT NULL,
        CompanyEvidence nvarchar(max) NULL,
        CompanyEvidenceByUserId uniqueidentifier NULL,
        CompanyEvidenceAt datetimeoffset NULL,
        CreatedByAuditorUserId uniqueidentifier NOT NULL,
        VerifiedByAuditorUserId uniqueidentifier NULL,
        VerifiedAt datetimeoffset NULL,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_AuditorFindings_CreatedAt DEFAULT (sysutcdatetime()),
        UpdatedAt datetimeoffset NOT NULL CONSTRAINT DF_AuditorFindings_UpdatedAt DEFAULT (sysutcdatetime()),
        CONSTRAINT PK_AuditorFindings PRIMARY KEY (Id),
        CONSTRAINT FK_AuditorFindings_Assignment FOREIGN KEY (AssignmentId) REFERENCES dbo.AuditorAssignments(Id),
        CONSTRAINT FK_AuditorFindings_TargetOrganization FOREIGN KEY (TargetOrganizationId) REFERENCES dbo.Organizations(Id),
        CONSTRAINT FK_AuditorFindings_JobReport FOREIGN KEY (JobReportId) REFERENCES dbo.JobReports(Id),
        CONSTRAINT FK_AuditorFindings_CreatedBy FOREIGN KEY (CreatedByAuditorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_AuditorFindings_VerifiedBy FOREIGN KEY (VerifiedByAuditorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_AuditorFindings_EvidenceBy FOREIGN KEY (CompanyEvidenceByUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_AuditorFindings_Category CHECK (Category IN (N'A', N'An', N'Anb', N'IR')),
        CONSTRAINT CK_AuditorFindings_Status CHECK (Status IN (N'Open', N'AwaitingEvidence', N'ReadyForVerification', N'Closed')),
        CONSTRAINT CK_AuditorFindings_Description CHECK (LEN(LTRIM(RTRIM(Description))) > 0),
        CONSTRAINT CK_AuditorFindings_Verification CHECK
        (
            (Status <> N'Closed' AND VerifiedByAuditorUserId IS NULL AND VerifiedAt IS NULL)
            OR
            (Status = N'Closed' AND VerifiedByAuditorUserId IS NOT NULL AND VerifiedAt IS NOT NULL)
        )
    );

    CREATE INDEX IX_AuditorFindings_Organization_Job_Status
        ON dbo.AuditorFindings (TargetOrganizationId, JobReportId, Status, UpdatedAt DESC)
        INCLUDE (AssignmentId, Category, DueAt);

    CREATE INDEX IX_AuditorFindings_Assignment_Status
        ON dbo.AuditorFindings (AssignmentId, Status, UpdatedAt DESC)
        INCLUDE (JobReportId, Category, DueAt);
END;

IF OBJECT_ID(N'dbo.AuditorEvents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditorEvents
    (
        Id uniqueidentifier NOT NULL,
        AssignmentId uniqueidentifier NULL,
        TargetOrganizationId uniqueidentifier NOT NULL,
        JobReportId uniqueidentifier NULL,
        FindingId uniqueidentifier NULL,
        ActorUserId uniqueidentifier NOT NULL,
        EventType nvarchar(80) NOT NULL,
        DetailsJson nvarchar(max) NULL,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_AuditorEvents_CreatedAt DEFAULT (sysutcdatetime()),
        CONSTRAINT PK_AuditorEvents PRIMARY KEY (Id),
        CONSTRAINT FK_AuditorEvents_Assignment FOREIGN KEY (AssignmentId) REFERENCES dbo.AuditorAssignments(Id),
        CONSTRAINT FK_AuditorEvents_TargetOrganization FOREIGN KEY (TargetOrganizationId) REFERENCES dbo.Organizations(Id),
        CONSTRAINT FK_AuditorEvents_JobReport FOREIGN KEY (JobReportId) REFERENCES dbo.JobReports(Id),
        CONSTRAINT FK_AuditorEvents_Finding FOREIGN KEY (FindingId) REFERENCES dbo.AuditorFindings(Id),
        CONSTRAINT FK_AuditorEvents_Actor FOREIGN KEY (ActorUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT CK_AuditorEvents_DetailsJson CHECK (DetailsJson IS NULL OR ISJSON(DetailsJson) = 1)
    );

    CREATE INDEX IX_AuditorEvents_Assignment_CreatedAt
        ON dbo.AuditorEvents (AssignmentId, CreatedAt DESC)
        INCLUDE (EventType, TargetOrganizationId, JobReportId, FindingId, ActorUserId);

    CREATE INDEX IX_AuditorEvents_Organization_Job_CreatedAt
        ON dbo.AuditorEvents (TargetOrganizationId, JobReportId, CreatedAt DESC)
        INCLUDE (EventType, AssignmentId, FindingId, ActorUserId);
END;

COMMIT TRANSACTION;
