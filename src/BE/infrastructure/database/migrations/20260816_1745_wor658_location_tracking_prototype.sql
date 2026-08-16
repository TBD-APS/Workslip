IF OBJECT_ID(N'dbo.LocationTrackingSessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.LocationTrackingSessions
    (
        Id uniqueidentifier NOT NULL,
        OrganizationId uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        StartedAt datetimeoffset NOT NULL,
        EndedAt datetimeoffset NULL,
        Source nvarchar(20) NOT NULL,
        Status nvarchar(20) NOT NULL,
        CONSTRAINT PK_LocationTrackingSessions PRIMARY KEY (Id),
        CONSTRAINT CK_LocationTrackingSessions_Source CHECK (Source IN (N'Phone', N'Vehicle')),
        CONSTRAINT CK_LocationTrackingSessions_Status CHECK (Status IN (N'Active', N'Stopped')),
        CONSTRAINT FK_LocationTrackingSessions_Organizations FOREIGN KEY (OrganizationId) REFERENCES dbo.Organizations(Id),
        CONSTRAINT FK_LocationTrackingSessions_Users FOREIGN KEY (OrganizationId, UserId) REFERENCES dbo.Users(OrganizationId, Id)
    );

    CREATE INDEX IX_LocationTrackingSessions_Organization_User_Status
        ON dbo.LocationTrackingSessions(OrganizationId, UserId, Status);
END;

IF OBJECT_ID(N'dbo.EmployeeLastLocations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmployeeLastLocations
    (
        OrganizationId uniqueidentifier NOT NULL,
        UserId uniqueidentifier NOT NULL,
        SessionId uniqueidentifier NOT NULL,
        CapturedAt datetimeoffset NOT NULL,
        Latitude decimal(9,6) NOT NULL,
        Longitude decimal(9,6) NOT NULL,
        AccuracyMeters decimal(10,2) NULL,
        UpdatedAt datetimeoffset NOT NULL CONSTRAINT DF_EmployeeLastLocations_UpdatedAt DEFAULT sysutcdatetime(),
        CONSTRAINT PK_EmployeeLastLocations PRIMARY KEY (OrganizationId, UserId),
        CONSTRAINT FK_EmployeeLastLocations_Organizations FOREIGN KEY (OrganizationId) REFERENCES dbo.Organizations(Id),
        CONSTRAINT FK_EmployeeLastLocations_Users FOREIGN KEY (OrganizationId, UserId) REFERENCES dbo.Users(OrganizationId, Id),
        CONSTRAINT FK_EmployeeLastLocations_Session FOREIGN KEY (SessionId) REFERENCES dbo.LocationTrackingSessions(Id),
        CONSTRAINT CK_EmployeeLastLocations_Latitude CHECK (Latitude BETWEEN -90 AND 90),
        CONSTRAINT CK_EmployeeLastLocations_Longitude CHECK (Longitude BETWEEN -180 AND 180),
        CONSTRAINT CK_EmployeeLastLocations_Accuracy CHECK (AccuracyMeters IS NULL OR AccuracyMeters >= 0)
    );

    CREATE INDEX IX_EmployeeLastLocations_Organization_CapturedAt
        ON dbo.EmployeeLastLocations(OrganizationId, CapturedAt DESC);
END;