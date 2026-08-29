-- Explicit production schema baseline.
--
-- Generated from the SQL Server EF model at the explicit-migration cutover commit
-- 1b962d23cb23bbdf20e7c8ea1be78bff9ca59764, immediately before WOR-385. The
-- baseline is deliberately ordered before the first forward migration so an empty
-- tenant database gets the historical schema that those migrations expect.
--
-- Existing complete Workslip databases are not modified. A partial database fails
-- closed: treating it as fresh could overwrite an unknown or interrupted schema.
DECLARE @baselineTables TABLE
(
    Name sysname NOT NULL PRIMARY KEY
);

INSERT INTO @baselineTables (Name)
VALUES
    (N'ControlCategories'),
    (N'ControlPoints'),
    (N'IdempotencyRecords'),
    (N'JobClosureFlags'),
    (N'JobWorkKinds'),
    (N'Organizations'),
    (N'Customers'),
    (N'InstallationTypeDefinitions'),
    (N'InviteTokens'),
    (N'Users'),
    (N'InstallationTypeDefinitionMappings'),
    (N'JobReports'),
    (N'NotificationQueue'),
    (N'PushSubscriptions'),
    (N'JobAssignments'),
    (N'JobEvents'),
    (N'JobReportClosureFlags'),
    (N'JobReportInstallations'),
    (N'JobReportLinks'),
    (N'JobViews'),
    (N'Worksheets'),
    (N'NotificationDeliveryLog'),
    (N'JobReportInstallationCategories'),
    (N'JobReportInstallationControlPoints');

DECLARE @presentBaselineTableCount int =
(
    SELECT COUNT(*)
    FROM @baselineTables AS expected
    INNER JOIN sys.tables AS actual
        ON actual.name = expected.Name
    INNER JOIN sys.schemas AS schemaInfo
        ON schemaInfo.schema_id = actual.schema_id
       AND schemaInfo.name = N'dbo'
);

IF @presentBaselineTableCount = 0
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM sys.tables AS actual
        INNER JOIN sys.schemas AS schemaInfo
            ON schemaInfo.schema_id = actual.schema_id
        WHERE schemaInfo.name = N'dbo'
          AND actual.is_ms_shipped = 0
          AND actual.name <> N'WorkslipSchemaMigrations'
    )
    BEGIN
        THROW 51384, 'Workslip explicit schema baseline found non-Workslip tables in an otherwise empty database.', 1;
    END;

CREATE TABLE [dbo].[ControlCategories] (
    [Id] uniqueidentifier NOT NULL,
    [OrganizationId] uniqueidentifier NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [SortOrder] int NOT NULL DEFAULT 0,
    CONSTRAINT [PK_ControlCategories] PRIMARY KEY ([Id])
);


CREATE TABLE [dbo].[ControlPoints] (
    [Id] uniqueidentifier NOT NULL,
    [OrganizationId] uniqueidentifier NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [SortOrder] int NOT NULL DEFAULT 0,
    CONSTRAINT [PK_ControlPoints] PRIMARY KEY ([Id])
);


CREATE TABLE [dbo].[IdempotencyRecords] (
    [Id] uniqueidentifier NOT NULL,
    [Scope] nvarchar(200) NOT NULL,
    [Key] nvarchar(128) NOT NULL,
    [RequestHash] nvarchar(64) NOT NULL,
    [ReservationToken] nvarchar(64) NOT NULL,
    [Completed] bit NOT NULL,
    [StatusCode] int NOT NULL,
    [ResponseJson] nvarchar(max) NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [ExpiresAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_IdempotencyRecords] PRIMARY KEY ([Id])
);


CREATE TABLE [dbo].[JobClosureFlags] (
    [Id] uniqueidentifier NOT NULL,
    [NormalizedLabel] nvarchar(80) NOT NULL,
    [Label] nvarchar(160) NOT NULL,
    [IsExclusive] bit NOT NULL,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [SortOrder] int NOT NULL DEFAULT 0,
    [UpdatedAt] datetimeoffset NOT NULL DEFAULT (sysutcdatetime()),
    CONSTRAINT [PK_JobClosureFlags] PRIMARY KEY ([Id])
);


CREATE TABLE [dbo].[JobWorkKinds] (
    [Id] uniqueidentifier NOT NULL,
    [NormalizedLabel] nvarchar(80) NOT NULL,
    [Label] nvarchar(160) NOT NULL,
    [RequiresCustomWorkKind] bit NOT NULL,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [SortOrder] int NOT NULL DEFAULT 0,
    CONSTRAINT [PK_JobWorkKinds] PRIMARY KEY ([Id])
);


CREATE TABLE [dbo].[Organizations] (
    [Id] uniqueidentifier NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Cvr] nvarchar(8) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL DEFAULT (sysutcdatetime()),
    [UpdatedAt] datetimeoffset NOT NULL DEFAULT (sysutcdatetime()),
    CONSTRAINT [PK_Organizations] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Organizations_Cvr_8Digits] CHECK (Cvr like '[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]')
);


CREATE TABLE [dbo].[Customers] (
    [Id] uniqueidentifier NOT NULL,
    [OrganizationId] uniqueidentifier NOT NULL,
    [CustomerNumber] nvarchar(max) NULL,
    [Name] nvarchar(240) NOT NULL,
    [Address] nvarchar(500) NULL,
    [ZipCode] nvarchar(max) NULL,
    [City] nvarchar(max) NULL,
    [Country] nvarchar(max) NULL,
    [Email] nvarchar(320) NULL,
    [ContactPerson] nvarchar(200) NULL,
    [Phone] nvarchar(80) NULL,
    [IsFavorite] bit NOT NULL DEFAULT CAST(0 AS bit),
    [CreatedAt] datetimeoffset NOT NULL DEFAULT (sysutcdatetime()),
    [UpdatedAt] datetimeoffset NOT NULL DEFAULT (sysutcdatetime()),
    CONSTRAINT [PK_Customers] PRIMARY KEY ([Id]),
    CONSTRAINT [UX_Customers_Organization_Id] UNIQUE ([OrganizationId], [Id]),
    CONSTRAINT [FK_Customers_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Organizations] ([Id]) ON DELETE NO ACTION
);


CREATE TABLE [dbo].[InstallationTypeDefinitions] (
    [Id] uniqueidentifier NOT NULL,
    [OrganizationId] uniqueidentifier NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [SortOrder] int NOT NULL DEFAULT 0,
    CONSTRAINT [PK_InstallationTypeDefinitions] PRIMARY KEY ([Id]),
    CONSTRAINT [AK_InstallationTypeDefinitions_OrganizationId_Id] UNIQUE ([OrganizationId], [Id]),
    CONSTRAINT [FK_InstallationTypeDefinitions_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Organizations] ([Id]) ON DELETE NO ACTION
);


CREATE TABLE [dbo].[InviteTokens] (
    [Id] uniqueidentifier NOT NULL,
    [OrganizationId] uniqueidentifier NOT NULL,
    [Email] nvarchar(320) NOT NULL,
    [Token] nvarchar(64) NOT NULL,
    [Role] nvarchar(80) NULL,
    [ExpiresAt] datetimeoffset NOT NULL,
    [Consumed] bit NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL DEFAULT (sysutcdatetime()),
    [OpenedAt] datetimeoffset NULL,
    [AcceptedAt] datetimeoffset NULL,
    [RevokedAt] datetimeoffset NULL,
    [EntraUserId] nvarchar(80) NULL,
    [EntraEmail] nvarchar(320) NULL,
    [EntraCreatedByInvite] bit NOT NULL,
    [EntraProvisionedAt] datetimeoffset NULL,
    [EntraCleanedAt] datetimeoffset NULL,
    CONSTRAINT [PK_InviteTokens] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_InviteTokens_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Organizations] ([Id]) ON DELETE NO ACTION
);


CREATE TABLE [dbo].[Users] (
    [Id] uniqueidentifier NOT NULL,
    [OrganizationId] uniqueidentifier NOT NULL,
    [Email] nvarchar(320) NOT NULL,
    [DisplayName] nvarchar(200) NOT NULL,
    [EntraId] nvarchar(80) NOT NULL,
    [EntraEmail] nvarchar(200) NOT NULL,
    [Phone] nvarchar(80) NOT NULL,
    [Role] nvarchar(80) NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL DEFAULT (sysutcdatetime()),
    [UpdatedAt] datetimeoffset NOT NULL DEFAULT (sysutcdatetime()),
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id]),
    CONSTRAINT [AK_Users_OrganizationId_Id] UNIQUE ([OrganizationId], [Id]),
    CONSTRAINT [FK_Users_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Organizations] ([Id]) ON DELETE NO ACTION
);


CREATE TABLE [dbo].[InstallationTypeDefinitionMappings] (
    [InstallationTypeDefinitionId] uniqueidentifier NOT NULL,
    [ControlCategoryId] uniqueidentifier NOT NULL,
    [ControlPointId] uniqueidentifier NOT NULL,
    [SortOrder] int NOT NULL DEFAULT 0,
    [IsRequired] bit NOT NULL DEFAULT CAST(0 AS bit),
    CONSTRAINT [PK_InstallationTypeDefinitionMappings] PRIMARY KEY ([InstallationTypeDefinitionId], [ControlCategoryId], [ControlPointId]),
    CONSTRAINT [FK_InstallationTypeDefinitionMappings_ControlCategories_ControlCategoryId] FOREIGN KEY ([ControlCategoryId]) REFERENCES [dbo].[ControlCategories] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InstallationTypeDefinitionMappings_ControlPoints_ControlPointId] FOREIGN KEY ([ControlPointId]) REFERENCES [dbo].[ControlPoints] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_InstallationTypeDefinitionMappings_InstallationTypeDefinitions_InstallationTypeDefinitionId] FOREIGN KEY ([InstallationTypeDefinitionId]) REFERENCES [dbo].[InstallationTypeDefinitions] ([Id]) ON DELETE CASCADE
);


CREATE TABLE [dbo].[JobReports] (
    [Id] uniqueidentifier NOT NULL,
    [OrganizationId] uniqueidentifier NOT NULL,
    [CustomerId] uniqueidentifier NULL,
    [CustomerName] nvarchar(max) NULL,
    [CustomerEmail] nvarchar(max) NULL,
    [CustomerPhone] nvarchar(max) NULL,
    [CustomerAddress] nvarchar(max) NULL,
    [CustomerContactPerson] nvarchar(max) NULL,
    [DestinationAddress] nvarchar(max) NULL,
    [DestinationZipCode] nvarchar(10) NULL,
    [DestinationCity] nvarchar(200) NULL,
    [ReportNumber] nvarchar(80) NULL,
    [Status] nvarchar(40) NOT NULL,
    [JobType] nvarchar(max) NOT NULL,
    [ReportDate] date NULL,
    [TaskDescription] nvarchar(max) NULL,
    [CustomerObservations] nvarchar(max) NULL,
    [TechnicalObservations] nvarchar(max) NULL,
    [WorkKindId] uniqueidentifier NULL,
    [CustomWorkKind] nvarchar(250) NULL,
    [Remarks] nvarchar(max) NULL,
    [IsSoftDeleted] bit NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [UpdatedAt] datetimeoffset NOT NULL,
    [DeletionScheduledAt] datetimeoffset NULL,
    [SubmittedAt] datetimeoffset NULL,
    [SubmittedByUserId] uniqueidentifier NULL,
    [RejectionNote] nvarchar(max) NULL,
    CONSTRAINT [PK_JobReports] PRIMARY KEY ([Id]),
    CONSTRAINT [AK_JobReports_OrganizationId_Id] UNIQUE ([OrganizationId], [Id]),
    CONSTRAINT [CK_JobReports_Status] CHECK (Status in ('Draft', 'InReview', 'Approved', 'Rejected')),
    CONSTRAINT [FK_JobReports_Customers_OrganizationId_CustomerId] FOREIGN KEY ([OrganizationId], [CustomerId]) REFERENCES [dbo].[Customers] ([OrganizationId], [Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_JobReports_JobWorkKinds_WorkKindId] FOREIGN KEY ([WorkKindId]) REFERENCES [dbo].[JobWorkKinds] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_JobReports_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Organizations] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_JobReports_Users_OrganizationId_SubmittedByUserId] FOREIGN KEY ([OrganizationId], [SubmittedByUserId]) REFERENCES [dbo].[Users] ([OrganizationId], [Id]) ON DELETE NO ACTION
);


CREATE TABLE [dbo].[NotificationQueue] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [NotificationType] nvarchar(100) NOT NULL,
    [PayloadJson] nvarchar(max) NOT NULL,
    [Status] nvarchar(50) NOT NULL,
    [RetryCount] int NOT NULL DEFAULT 0,
    [CreatedUtc] datetimeoffset NOT NULL DEFAULT (sysutcdatetime()),
    [ProcessingStartedUtc] datetimeoffset NULL,
    [NextAttemptUtc] datetimeoffset NOT NULL,
    [CompletedUtc] datetimeoffset NULL,
    [ReadUtc] datetimeoffset NULL,
    [LastError] nvarchar(max) NULL,
    CONSTRAINT [PK_NotificationQueue] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_NotificationQueue_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE NO ACTION
);


CREATE TABLE [dbo].[PushSubscriptions] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [Endpoint] nvarchar(2000) NOT NULL,
    [P256Dh] nvarchar(200) NOT NULL,
    [Auth] nvarchar(200) NOT NULL,
    [UserAgent] nvarchar(500) NULL,
    [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
    [CreatedUtc] datetimeoffset NOT NULL DEFAULT (sysutcdatetime()),
    [LastSeenUtc] datetimeoffset NOT NULL DEFAULT (sysutcdatetime()),
    CONSTRAINT [PK_PushSubscriptions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PushSubscriptions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE NO ACTION
);


CREATE TABLE [dbo].[JobAssignments] (
    [Id] uniqueidentifier NOT NULL,
    [OrganizationId] uniqueidentifier NOT NULL,
    [ReportId] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [AssignedByUserId] uniqueidentifier NULL,
    [AssignedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_JobAssignments] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_JobAssignments_JobReports_OrganizationId_ReportId] FOREIGN KEY ([OrganizationId], [ReportId]) REFERENCES [dbo].[JobReports] ([OrganizationId], [Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_JobAssignments_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Organizations] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_JobAssignments_Users_OrganizationId_AssignedByUserId] FOREIGN KEY ([OrganizationId], [AssignedByUserId]) REFERENCES [dbo].[Users] ([OrganizationId], [Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_JobAssignments_Users_OrganizationId_UserId] FOREIGN KEY ([OrganizationId], [UserId]) REFERENCES [dbo].[Users] ([OrganizationId], [Id]) ON DELETE NO ACTION
);


CREATE TABLE [dbo].[JobEvents] (
    [Id] uniqueidentifier NOT NULL,
    [OrganizationId] uniqueidentifier NOT NULL,
    [ReportId] uniqueidentifier NULL,
    [ActorId] uniqueidentifier NULL,
    [EventType] nvarchar(80) NOT NULL,
    [Summary] nvarchar(500) NULL,
    [BeforeJson] nvarchar(max) NULL,
    [AfterJson] nvarchar(max) NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_JobEvents] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_JobEvents_AfterJson_IsJson] CHECK (AfterJson is null or isjson(AfterJson) = 1),
    CONSTRAINT [CK_JobEvents_BeforeJson_IsJson] CHECK (BeforeJson is null or isjson(BeforeJson) = 1),
    CONSTRAINT [FK_JobEvents_JobReports_OrganizationId_ReportId] FOREIGN KEY ([OrganizationId], [ReportId]) REFERENCES [dbo].[JobReports] ([OrganizationId], [Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_JobEvents_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Organizations] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_JobEvents_Users_OrganizationId_ActorId] FOREIGN KEY ([OrganizationId], [ActorId]) REFERENCES [dbo].[Users] ([OrganizationId], [Id]) ON DELETE NO ACTION
);


CREATE TABLE [dbo].[JobReportClosureFlags] (
    [Id] uniqueidentifier NOT NULL,
    [JobReportId] uniqueidentifier NOT NULL,
    [OrganizationId] uniqueidentifier NOT NULL,
    [ClosureFlagId] uniqueidentifier NOT NULL,
    [SortOrder] int NOT NULL DEFAULT 0,
    CONSTRAINT [PK_JobReportClosureFlags] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_JobReportClosureFlags_JobClosureFlags_ClosureFlagId] FOREIGN KEY ([ClosureFlagId]) REFERENCES [dbo].[JobClosureFlags] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_JobReportClosureFlags_JobReports_JobReportId] FOREIGN KEY ([JobReportId]) REFERENCES [dbo].[JobReports] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_JobReportClosureFlags_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Organizations] ([Id]) ON DELETE NO ACTION
);


CREATE TABLE [dbo].[JobReportInstallations] (
    [Id] uniqueidentifier NOT NULL,
    [JobReportId] uniqueidentifier NOT NULL,
    [OrganizationId] uniqueidentifier NOT NULL,
    [InstallationTypeDefinitionId] uniqueidentifier NOT NULL,
    [SortOrder] int NOT NULL DEFAULT 0,
    CONSTRAINT [PK_JobReportInstallations] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_JobReportInstallations_InstallationTypeDefinitions_OrganizationId_InstallationTypeDefinitionId] FOREIGN KEY ([OrganizationId], [InstallationTypeDefinitionId]) REFERENCES [dbo].[InstallationTypeDefinitions] ([OrganizationId], [Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_JobReportInstallations_JobReports_OrganizationId_JobReportId] FOREIGN KEY ([OrganizationId], [JobReportId]) REFERENCES [dbo].[JobReports] ([OrganizationId], [Id]) ON DELETE CASCADE
);


CREATE TABLE [dbo].[JobReportLinks] (
    [Id] uniqueidentifier NOT NULL,
    [OrganizationId] uniqueidentifier NOT NULL,
    [SourceReportId] uniqueidentifier NOT NULL,
    [TargetReportId] uniqueidentifier NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL,
    [JobReportRowId] uniqueidentifier NULL,
    CONSTRAINT [PK_JobReportLinks] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_JobReportLinks_NoSelfLink] CHECK (SourceReportId != TargetReportId),
    CONSTRAINT [FK_JobReportLinks_JobReports_JobReportRowId] FOREIGN KEY ([JobReportRowId]) REFERENCES [dbo].[JobReports] ([Id]),
    CONSTRAINT [FK_JobReportLinks_JobReports_OrganizationId_SourceReportId] FOREIGN KEY ([OrganizationId], [SourceReportId]) REFERENCES [dbo].[JobReports] ([OrganizationId], [Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_JobReportLinks_JobReports_OrganizationId_TargetReportId] FOREIGN KEY ([OrganizationId], [TargetReportId]) REFERENCES [dbo].[JobReports] ([OrganizationId], [Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_JobReportLinks_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Organizations] ([Id]) ON DELETE NO ACTION
);


CREATE TABLE [dbo].[JobViews] (
    [Id] uniqueidentifier NOT NULL,
    [JobId] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [ViewType] nvarchar(50) NOT NULL,
    [ViewedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_JobViews] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_JobViews_JobReports_JobId] FOREIGN KEY ([JobId]) REFERENCES [dbo].[JobReports] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_JobViews_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [dbo].[Users] ([Id]) ON DELETE NO ACTION
);


CREATE TABLE [dbo].[Worksheets] (
    [Id] uniqueidentifier NOT NULL,
    [OrganizationId] uniqueidentifier NOT NULL,
    [JobId] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [WorkDate] date NOT NULL,
    [HoursWorked] decimal(5,2) NOT NULL,
    [SleptOnJob] bit NOT NULL,
    [CreatedAt] datetimeoffset NOT NULL DEFAULT (sysutcdatetime()),
    [UpdatedAt] datetimeoffset NOT NULL DEFAULT (sysutcdatetime()),
    CONSTRAINT [PK_Worksheets] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Worksheets_HoursWorked] CHECK (HoursWorked >= 0 and HoursWorked <= 24),
    CONSTRAINT [CK_Worksheets_HoursWorked_Increment] CHECK ((HoursWorked * 4) % 1 = 0),
    CONSTRAINT [FK_Worksheets_JobReports_OrganizationId_JobId] FOREIGN KEY ([OrganizationId], [JobId]) REFERENCES [dbo].[JobReports] ([OrganizationId], [Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Worksheets_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Organizations] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Worksheets_Users_OrganizationId_UserId] FOREIGN KEY ([OrganizationId], [UserId]) REFERENCES [dbo].[Users] ([OrganizationId], [Id]) ON DELETE NO ACTION
);


CREATE TABLE [dbo].[NotificationDeliveryLog] (
    [Id] uniqueidentifier NOT NULL,
    [NotificationId] uniqueidentifier NOT NULL,
    [SubscriptionId] uniqueidentifier NOT NULL,
    [Success] bit NOT NULL,
    [SentUtc] datetimeoffset NOT NULL DEFAULT (sysutcdatetime()),
    [ErrorMessage] nvarchar(max) NULL,
    CONSTRAINT [PK_NotificationDeliveryLog] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_NotificationDeliveryLog_PushSubscriptions_SubscriptionId] FOREIGN KEY ([SubscriptionId]) REFERENCES [dbo].[PushSubscriptions] ([Id]) ON DELETE NO ACTION
);


CREATE TABLE [dbo].[JobReportInstallationCategories] (
    [Id] uniqueidentifier NOT NULL,
    [JobReportInstallationId] uniqueidentifier NOT NULL,
    [ControlCategoryId] uniqueidentifier NOT NULL,
    [SortOrder] int NOT NULL DEFAULT 0,
    [IsIrrelevant] bit NOT NULL DEFAULT CAST(0 AS bit),
    CONSTRAINT [PK_JobReportInstallationCategories] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_JobReportInstallationCategories_ControlCategories_ControlCategoryId] FOREIGN KEY ([ControlCategoryId]) REFERENCES [dbo].[ControlCategories] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_JobReportInstallationCategories_JobReportInstallations_JobReportInstallationId] FOREIGN KEY ([JobReportInstallationId]) REFERENCES [dbo].[JobReportInstallations] ([Id]) ON DELETE CASCADE
);


CREATE TABLE [dbo].[JobReportInstallationControlPoints] (
    [JobReportInstallationCategoryId] uniqueidentifier NOT NULL,
    [ControlPointId] uniqueidentifier NOT NULL,
    [SortOrder] int NOT NULL DEFAULT 0,
    [IsRequired] bit NOT NULL DEFAULT CAST(0 AS bit),
    [IsChecked] bit NOT NULL DEFAULT CAST(0 AS bit),
    CONSTRAINT [PK_JobReportInstallationControlPoints] PRIMARY KEY ([JobReportInstallationCategoryId], [ControlPointId]),
    CONSTRAINT [FK_JobReportInstallationControlPoints_ControlPoints_ControlPointId] FOREIGN KEY ([ControlPointId]) REFERENCES [dbo].[ControlPoints] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_JobReportInstallationControlPoints_JobReportInstallationCategories_JobReportInstallationCategoryId] FOREIGN KEY ([JobReportInstallationCategoryId]) REFERENCES [dbo].[JobReportInstallationCategories] ([Id]) ON DELETE CASCADE
);


CREATE UNIQUE INDEX [IX_ControlCategories_OrganizationId_Name] ON [dbo].[ControlCategories] ([OrganizationId], [Name]);


CREATE INDEX [IX_ControlCategories_OrganizationId_SortOrder] ON [dbo].[ControlCategories] ([OrganizationId], [SortOrder]);


CREATE INDEX [IX_ControlPoints_OrganizationId_Name] ON [dbo].[ControlPoints] ([OrganizationId], [Name]);


CREATE INDEX [IX_IdempotencyRecords_ExpiresAt] ON [dbo].[IdempotencyRecords] ([ExpiresAt]);


CREATE UNIQUE INDEX [UX_IdempotencyRecords_Scope_Key] ON [dbo].[IdempotencyRecords] ([Scope], [Key]);


CREATE INDEX [IX_InstallationTypeDefinitionMappings_ControlCategoryId] ON [dbo].[InstallationTypeDefinitionMappings] ([ControlCategoryId]);


CREATE INDEX [IX_InstallationTypeDefinitionMappings_ControlPointId] ON [dbo].[InstallationTypeDefinitionMappings] ([ControlPointId]);


CREATE INDEX [IX_InstallationTypeDefinitionMappings_InstallationTypeDefinitionId_ControlCategoryId_SortOrder] ON [dbo].[InstallationTypeDefinitionMappings] ([InstallationTypeDefinitionId], [ControlCategoryId], [SortOrder]);


CREATE UNIQUE INDEX [IX_InstallationTypeDefinitions_OrganizationId_Name] ON [dbo].[InstallationTypeDefinitions] ([OrganizationId], [Name]);


CREATE INDEX [IX_InstallationTypeDefinitions_OrganizationId_SortOrder] ON [dbo].[InstallationTypeDefinitions] ([OrganizationId], [SortOrder]);


CREATE INDEX [IX_InviteTokens_Email] ON [dbo].[InviteTokens] ([Email]) WHERE [Consumed] = 0;


CREATE INDEX [IX_InviteTokens_OrganizationId] ON [dbo].[InviteTokens] ([OrganizationId]);


CREATE UNIQUE INDEX [UX_InviteTokens_Token] ON [dbo].[InviteTokens] ([Token]);


CREATE INDEX [IX_JobAssignments_OrganizationId_AssignedByUserId] ON [dbo].[JobAssignments] ([OrganizationId], [AssignedByUserId]);


CREATE INDEX [IX_JobAssignments_User] ON [dbo].[JobAssignments] ([OrganizationId], [UserId]);


CREATE UNIQUE INDEX [UX_JobAssignments_Report_User] ON [dbo].[JobAssignments] ([OrganizationId], [ReportId], [UserId]);


CREATE UNIQUE INDEX [UX_JobClosureFlags_Label] ON [dbo].[JobClosureFlags] ([Label]);


CREATE INDEX [IX_JobEvents_OrganizationId_ActorId] ON [dbo].[JobEvents] ([OrganizationId], [ActorId]);


CREATE INDEX [IX_JobEvents_Report_CreatedAt] ON [dbo].[JobEvents] ([OrganizationId], [ReportId], [CreatedAt] DESC);


CREATE INDEX [IX_JobReportClosureFlags_ClosureFlagId] ON [dbo].[JobReportClosureFlags] ([ClosureFlagId]);


CREATE INDEX [IX_JobReportClosureFlags_OrganizationId] ON [dbo].[JobReportClosureFlags] ([OrganizationId]);


CREATE UNIQUE INDEX [UX_JobReportClosureFlags_Report_Flag] ON [dbo].[JobReportClosureFlags] ([JobReportId], [ClosureFlagId]);


CREATE INDEX [IX_JobReportInstallationCategories_ControlCategoryId] ON [dbo].[JobReportInstallationCategories] ([ControlCategoryId]);


CREATE UNIQUE INDEX [IX_JobReportInstallationCategories_JobReportInstallationId_ControlCategoryId] ON [dbo].[JobReportInstallationCategories] ([JobReportInstallationId], [ControlCategoryId]);


CREATE INDEX [IX_JobReportInstallationCategories_JobReportInstallationId_SortOrder] ON [dbo].[JobReportInstallationCategories] ([JobReportInstallationId], [SortOrder]);


CREATE INDEX [IX_JobReportInstallationControlPoints_ControlPointId] ON [dbo].[JobReportInstallationControlPoints] ([ControlPointId]);


CREATE INDEX [IX_JobReportInstallationControlPoints_JobReportInstallationCategoryId_SortOrder] ON [dbo].[JobReportInstallationControlPoints] ([JobReportInstallationCategoryId], [SortOrder]);


CREATE INDEX [IX_JobReportInstallations_OrganizationId_InstallationTypeDefinitionId] ON [dbo].[JobReportInstallations] ([OrganizationId], [InstallationTypeDefinitionId]);


CREATE UNIQUE INDEX [IX_JobReportInstallations_OrganizationId_JobReportId_InstallationTypeDefinitionId] ON [dbo].[JobReportInstallations] ([OrganizationId], [JobReportId], [InstallationTypeDefinitionId]);


CREATE INDEX [IX_JobReportInstallations_OrganizationId_JobReportId_SortOrder] ON [dbo].[JobReportInstallations] ([OrganizationId], [JobReportId], [SortOrder]);


CREATE INDEX [IX_JobReportLinks_JobReportRowId] ON [dbo].[JobReportLinks] ([JobReportRowId]);


CREATE INDEX [IX_JobReportLinks_TargetReport] ON [dbo].[JobReportLinks] ([OrganizationId], [TargetReportId]);


CREATE UNIQUE INDEX [UX_JobReportLinks_Pair] ON [dbo].[JobReportLinks] ([OrganizationId], [SourceReportId], [TargetReportId]);


CREATE INDEX [IX_JobReports_DeletionScheduledAt] ON [dbo].[JobReports] ([DeletionScheduledAt]) WHERE [DeletionScheduledAt] is not null;


CREATE INDEX [IX_JobReports_Organization_Status_UpdatedAt] ON [dbo].[JobReports] ([OrganizationId], [Status], [UpdatedAt] DESC);


CREATE INDEX [IX_JobReports_Organization_SubmittedByUserId] ON [dbo].[JobReports] ([OrganizationId], [SubmittedByUserId]) WHERE [SubmittedByUserId] is not null;


CREATE INDEX [IX_JobReports_OrganizationId_CustomerId] ON [dbo].[JobReports] ([OrganizationId], [CustomerId]);


CREATE INDEX [IX_JobReports_WorkKindId] ON [dbo].[JobReports] ([WorkKindId]);


CREATE UNIQUE INDEX [UX_JobReports_Organization_ReportNumber] ON [dbo].[JobReports] ([OrganizationId], [ReportNumber]) WHERE [ReportNumber] IS NOT NULL;


CREATE UNIQUE INDEX [IX_JobViews_JobId_UserId_ViewType] ON [dbo].[JobViews] ([JobId], [UserId], [ViewType]);


CREATE INDEX [IX_JobViews_UserId] ON [dbo].[JobViews] ([UserId]);


CREATE UNIQUE INDEX [UX_JobWorkKinds_Label] ON [dbo].[JobWorkKinds] ([Label]);


CREATE INDEX [IX_NotificationDeliveryLog_SubscriptionId] ON [dbo].[NotificationDeliveryLog] ([SubscriptionId]);


CREATE INDEX [IX_NotificationQueue_Status_NextAttempt] ON [dbo].[NotificationQueue] ([Status], [NextAttemptUtc]);


CREATE INDEX [IX_NotificationQueue_UserId] ON [dbo].[NotificationQueue] ([UserId]);


CREATE UNIQUE INDEX [UX_Organizations_Cvr] ON [dbo].[Organizations] ([Cvr]);


CREATE INDEX [IX_PushSubscriptions_User_Active] ON [dbo].[PushSubscriptions] ([UserId], [IsActive]);


CREATE UNIQUE INDEX [UX_Users_Organization_Id] ON [dbo].[Users] ([OrganizationId], [Id]);


CREATE INDEX [IX_Worksheets_JobId] ON [dbo].[Worksheets] ([JobId]);


CREATE INDEX [IX_Worksheets_OrganizationId_JobId] ON [dbo].[Worksheets] ([OrganizationId], [JobId]);


CREATE INDEX [IX_Worksheets_OrganizationId_UserId] ON [dbo].[Worksheets] ([OrganizationId], [UserId]);


CREATE INDEX [IX_Worksheets_UserId] ON [dbo].[Worksheets] ([UserId]);


CREATE INDEX [IX_Worksheets_WorkDate] ON [dbo].[Worksheets] ([WorkDate]);


CREATE UNIQUE INDEX [UX_Worksheets_Organization_Id] ON [dbo].[Worksheets] ([OrganizationId], [Id]);

END;
IF @presentBaselineTableCount <> 0
   AND @presentBaselineTableCount <> 24
BEGIN
    THROW 51385, 'Workslip explicit schema baseline found a partial Workslip schema. Restore or repair the database before migration.', 1;
END;
