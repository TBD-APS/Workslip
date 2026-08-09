-- WOR-385
-- Introduce Filial under Organization and close the remaining installation snapshot tenant-integrity gap.
--
-- Deployment compatibility matters here: production migrations run before the new API package is deployed.
-- The four new ownership columns therefore remain nullable during this expand phase. Transitional INSERT
-- triggers populate them for the previous API version during the deploy window. A later contract migration
-- can make the columns physically NOT NULL and remove the triggers after WOR-385 is live everywhere.
--
-- Preflight must remain before the first schema/data mutation.

IF OBJECT_ID(N'dbo.Organizations', N'U') IS NULL
    THROW 51385, 'WOR-385 requires dbo.Organizations.', 1;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
    THROW 51385, 'WOR-385 requires dbo.Users.', 1;
IF OBJECT_ID(N'dbo.JobReports', N'U') IS NULL
    THROW 51385, 'WOR-385 requires dbo.JobReports.', 1;
IF OBJECT_ID(N'dbo.JobReportInstallations', N'U') IS NULL
    THROW 51385, 'WOR-385 requires dbo.JobReportInstallations.', 1;
IF OBJECT_ID(N'dbo.JobReportInstallationCategories', N'U') IS NULL
    THROW 51385, 'WOR-385 requires dbo.JobReportInstallationCategories.', 1;
IF OBJECT_ID(N'dbo.JobReportInstallationControlPoints', N'U') IS NULL
    THROW 51385, 'WOR-385 requires dbo.JobReportInstallationControlPoints.', 1;
IF OBJECT_ID(N'dbo.ControlCategories', N'U') IS NULL
    THROW 51385, 'WOR-385 requires dbo.ControlCategories.', 1;
IF OBJECT_ID(N'dbo.ControlPoints', N'U') IS NULL
    THROW 51385, 'WOR-385 requires dbo.ControlPoints.', 1;

DECLARE @invalidCategoryCount int;
DECLARE @invalidControlPointCount int;
DECLARE @errorMessage nvarchar(2048);

SELECT @invalidCategoryCount = COUNT(*)
FROM dbo.JobReportInstallationCategories AS snapshotCategory
LEFT JOIN dbo.JobReportInstallations AS installation
    ON installation.Id = snapshotCategory.JobReportInstallationId
LEFT JOIN dbo.ControlCategories AS controlCategory
    ON controlCategory.Id = snapshotCategory.ControlCategoryId
WHERE installation.Id IS NULL
   OR controlCategory.Id IS NULL
   OR installation.OrganizationId <> controlCategory.OrganizationId;

IF @invalidCategoryCount > 0
BEGIN
    SET @errorMessage = CONCAT(
        'WOR-385 blocked: ',
        @invalidCategoryCount,
        ' installation category snapshot row(s) have missing or cross-organization references.');
    THROW 51386, @errorMessage, 1;
END;

SELECT @invalidControlPointCount = COUNT(*)
FROM dbo.JobReportInstallationControlPoints AS snapshotPoint
LEFT JOIN dbo.JobReportInstallationCategories AS snapshotCategory
    ON snapshotCategory.Id = snapshotPoint.JobReportInstallationCategoryId
LEFT JOIN dbo.JobReportInstallations AS installation
    ON installation.Id = snapshotCategory.JobReportInstallationId
LEFT JOIN dbo.ControlPoints AS controlPoint
    ON controlPoint.Id = snapshotPoint.ControlPointId
WHERE snapshotCategory.Id IS NULL
   OR installation.Id IS NULL
   OR controlPoint.Id IS NULL
   OR installation.OrganizationId <> controlPoint.OrganizationId;

IF @invalidControlPointCount > 0
BEGIN
    SET @errorMessage = CONCAT(
        'WOR-385 blocked: ',
        @invalidControlPointCount,
        ' installation control-point snapshot row(s) have missing or cross-organization references.');
    THROW 51387, @errorMessage, 1;
END;

IF OBJECT_ID(N'dbo.OrganizationFilials', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OrganizationFilials
    (
        Id uniqueidentifier NOT NULL,
        OrganizationId uniqueidentifier NOT NULL,
        Name nvarchar(200) NOT NULL,
        IsDefault bit NOT NULL,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_OrganizationFilials_CreatedAt DEFAULT sysutcdatetime(),
        UpdatedAt datetimeoffset NOT NULL CONSTRAINT DF_OrganizationFilials_UpdatedAt DEFAULT sysutcdatetime(),
        CONSTRAINT PK_OrganizationFilials PRIMARY KEY (Id),
        CONSTRAINT AK_OrganizationFilials_OrganizationId_Id UNIQUE (OrganizationId, Id),
        CONSTRAINT FK_OrganizationFilials_Organizations_OrganizationId
            FOREIGN KEY (OrganizationId) REFERENCES dbo.Organizations (Id)
    );
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.OrganizationFilials')
      AND name = N'UX_OrganizationFilials_DefaultPerOrganization')
BEGIN
    CREATE UNIQUE INDEX UX_OrganizationFilials_DefaultPerOrganization
        ON dbo.OrganizationFilials (OrganizationId)
        WHERE IsDefault = 1;
END;

-- Default Filial IDs deliberately reuse Organization IDs. The mapping is deterministic
-- for backfill/retry purposes; later non-default Filials use independent GUIDs.
INSERT INTO dbo.OrganizationFilials
(
    Id,
    OrganizationId,
    Name,
    IsDefault,
    CreatedAt,
    UpdatedAt
)
SELECT
    organization.Id,
    organization.Id,
    LEFT(organization.Name, 200),
    1,
    organization.CreatedAt,
    organization.UpdatedAt
FROM dbo.Organizations AS organization
WHERE NOT EXISTS (
    SELECT 1
    FROM dbo.OrganizationFilials AS filial
    WHERE filial.OrganizationId = organization.Id
      AND filial.IsDefault = 1);

IF EXISTS (
    SELECT organization.Id
    FROM dbo.Organizations AS organization
    LEFT JOIN dbo.OrganizationFilials AS filial
        ON filial.OrganizationId = organization.Id
       AND filial.IsDefault = 1
    GROUP BY organization.Id
    HAVING COUNT(filial.Id) <> 1)
BEGIN
    THROW 51388, 'WOR-385 could not establish exactly one default filial for every organization.', 1;
END;

IF COL_LENGTH(N'dbo.Users', N'FilialId') IS NULL
    ALTER TABLE dbo.Users ADD FilialId uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.JobReports', N'FilialId') IS NULL
    ALTER TABLE dbo.JobReports ADD FilialId uniqueidentifier NULL;

-- SQL Server compiles a batch before executing preceding ALTER TABLE statements.
-- Defer all references to newly-added FilialId columns until after those ALTERs execute.
EXEC(N'
UPDATE users
SET FilialId = filial.Id
FROM dbo.Users AS users
INNER JOIN dbo.OrganizationFilials AS filial
    ON filial.OrganizationId = users.OrganizationId
   AND filial.IsDefault = 1
WHERE users.FilialId IS NULL;

UPDATE jobs
SET FilialId = filial.Id
FROM dbo.JobReports AS jobs
INNER JOIN dbo.OrganizationFilials AS filial
    ON filial.OrganizationId = jobs.OrganizationId
   AND filial.IsDefault = 1
WHERE jobs.FilialId IS NULL;

IF EXISTS (
    SELECT 1
    FROM dbo.Users AS users
    LEFT JOIN dbo.OrganizationFilials AS filial
        ON filial.OrganizationId = users.OrganizationId
       AND filial.Id = users.FilialId
    WHERE users.FilialId IS NULL OR filial.Id IS NULL)
BEGIN
    THROW 51389, ''WOR-385 could not assign every user to a filial in the same organization.'', 1;
END;

IF EXISTS (
    SELECT 1
    FROM dbo.JobReports AS jobs
    LEFT JOIN dbo.OrganizationFilials AS filial
        ON filial.OrganizationId = jobs.OrganizationId
       AND filial.Id = jobs.FilialId
    WHERE jobs.FilialId IS NULL OR filial.Id IS NULL)
BEGIN
    THROW 51390, ''WOR-385 could not assign every job to a filial in the same organization.'', 1;
END;

IF OBJECT_ID(N''dbo.FK_Users_OrganizationFilials_OrganizationId_FilialId'', N''F'') IS NULL
BEGIN
    ALTER TABLE dbo.Users WITH CHECK
        ADD CONSTRAINT FK_Users_OrganizationFilials_OrganizationId_FilialId
        FOREIGN KEY (OrganizationId, FilialId)
        REFERENCES dbo.OrganizationFilials (OrganizationId, Id);
END;

IF OBJECT_ID(N''dbo.FK_JobReports_OrganizationFilials_OrganizationId_FilialId'', N''F'') IS NULL
BEGIN
    ALTER TABLE dbo.JobReports WITH CHECK
        ADD CONSTRAINT FK_JobReports_OrganizationFilials_OrganizationId_FilialId
        FOREIGN KEY (OrganizationId, FilialId)
        REFERENCES dbo.OrganizationFilials (OrganizationId, Id);
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N''dbo.Users'')
      AND name = N''IX_Users_Organization_FilialId'')
BEGIN
    CREATE INDEX IX_Users_Organization_FilialId
        ON dbo.Users (OrganizationId, FilialId);
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N''dbo.JobReports'')
      AND name = N''IX_JobReports_Organization_FilialId'')
BEGIN
    CREATE INDEX IX_JobReports_Organization_FilialId
        ON dbo.JobReports (OrganizationId, FilialId);
END;
');

IF COL_LENGTH(N'dbo.JobReportInstallationCategories', N'OrganizationId') IS NULL
    ALTER TABLE dbo.JobReportInstallationCategories ADD OrganizationId uniqueidentifier NULL;
IF COL_LENGTH(N'dbo.JobReportInstallationControlPoints', N'OrganizationId') IS NULL
    ALTER TABLE dbo.JobReportInstallationControlPoints ADD OrganizationId uniqueidentifier NULL;

-- Same deferred-compilation rule for the newly-added snapshot OrganizationId columns.
EXEC(N'
UPDATE snapshotCategory
SET OrganizationId = installation.OrganizationId
FROM dbo.JobReportInstallationCategories AS snapshotCategory
INNER JOIN dbo.JobReportInstallations AS installation
    ON installation.Id = snapshotCategory.JobReportInstallationId
WHERE snapshotCategory.OrganizationId IS NULL;

UPDATE snapshotPoint
SET OrganizationId = snapshotCategory.OrganizationId
FROM dbo.JobReportInstallationControlPoints AS snapshotPoint
INNER JOIN dbo.JobReportInstallationCategories AS snapshotCategory
    ON snapshotCategory.Id = snapshotPoint.JobReportInstallationCategoryId
WHERE snapshotPoint.OrganizationId IS NULL;

IF EXISTS (SELECT 1 FROM dbo.JobReportInstallationCategories WHERE OrganizationId IS NULL)
    THROW 51391, ''WOR-385 could not derive OrganizationId for every category snapshot.'', 1;
IF EXISTS (SELECT 1 FROM dbo.JobReportInstallationControlPoints WHERE OrganizationId IS NULL)
    THROW 51392, ''WOR-385 could not derive OrganizationId for every control-point snapshot.'', 1;

IF NOT EXISTS (
    SELECT 1
    FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N''dbo.JobReportInstallations'')
      AND name = N''AK_JobReportInstallations_OrganizationId_Id'')
BEGIN
    ALTER TABLE dbo.JobReportInstallations
        ADD CONSTRAINT AK_JobReportInstallations_OrganizationId_Id
        UNIQUE (OrganizationId, Id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N''dbo.ControlCategories'')
      AND name = N''AK_ControlCategories_OrganizationId_Id'')
BEGIN
    ALTER TABLE dbo.ControlCategories
        ADD CONSTRAINT AK_ControlCategories_OrganizationId_Id
        UNIQUE (OrganizationId, Id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N''dbo.ControlPoints'')
      AND name = N''AK_ControlPoints_OrganizationId_Id'')
BEGIN
    ALTER TABLE dbo.ControlPoints
        ADD CONSTRAINT AK_ControlPoints_OrganizationId_Id
        UNIQUE (OrganizationId, Id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N''dbo.JobReportInstallationCategories'')
      AND name = N''AK_JobReportInstallationCategories_OrganizationId_Id'')
BEGIN
    ALTER TABLE dbo.JobReportInstallationCategories
        ADD CONSTRAINT AK_JobReportInstallationCategories_OrganizationId_Id
        UNIQUE (OrganizationId, Id);
END;

-- Keep the existing simple foreign keys because EF currently models them and they own
-- cascade behavior. The additional composite FKs below are the tenant-integrity guard.
IF OBJECT_ID(N''dbo.FK_JobReportInstallationCategories_JobReportInstallations_Organization'', N''F'') IS NULL
BEGIN
    ALTER TABLE dbo.JobReportInstallationCategories WITH CHECK
        ADD CONSTRAINT FK_JobReportInstallationCategories_JobReportInstallations_Organization
        FOREIGN KEY (OrganizationId, JobReportInstallationId)
        REFERENCES dbo.JobReportInstallations (OrganizationId, Id);
END;

IF OBJECT_ID(N''dbo.FK_JobReportInstallationCategories_ControlCategories_Organization'', N''F'') IS NULL
BEGIN
    ALTER TABLE dbo.JobReportInstallationCategories WITH CHECK
        ADD CONSTRAINT FK_JobReportInstallationCategories_ControlCategories_Organization
        FOREIGN KEY (OrganizationId, ControlCategoryId)
        REFERENCES dbo.ControlCategories (OrganizationId, Id);
END;

IF OBJECT_ID(N''dbo.FK_JobReportInstallationControlPoints_Categories_Organization'', N''F'') IS NULL
BEGIN
    ALTER TABLE dbo.JobReportInstallationControlPoints WITH CHECK
        ADD CONSTRAINT FK_JobReportInstallationControlPoints_Categories_Organization
        FOREIGN KEY (OrganizationId, JobReportInstallationCategoryId)
        REFERENCES dbo.JobReportInstallationCategories (OrganizationId, Id);
END;

IF OBJECT_ID(N''dbo.FK_JobReportInstallationControlPoints_ControlPoints_Organization'', N''F'') IS NULL
BEGIN
    ALTER TABLE dbo.JobReportInstallationControlPoints WITH CHECK
        ADD CONSTRAINT FK_JobReportInstallationControlPoints_ControlPoints_Organization
        FOREIGN KEY (OrganizationId, ControlPointId)
        REFERENCES dbo.ControlPoints (OrganizationId, Id);
END;
');

-- Transitional INSERT compatibility for the previous API version. These triggers are
-- intentionally narrow: they only fill newly introduced ownership columns when omitted.
EXEC(N'
CREATE OR ALTER TRIGGER dbo.TR_WOR385_Users_DefaultFilial
ON dbo.Users
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    -- The previous API creates Organization + creator without knowing about Filial.
    -- Lazily establish the deterministic default here so that onboarding remains
    -- write-safe between this migration and deployment of the Filial-aware API.
    INSERT INTO dbo.OrganizationFilials
    (
        Id,
        OrganizationId,
        Name,
        IsDefault,
        CreatedAt,
        UpdatedAt
    )
    SELECT DISTINCT
        organization.Id,
        organization.Id,
        LEFT(organization.Name, 200),
        1,
        organization.CreatedAt,
        organization.UpdatedAt
    FROM inserted AS insertedUser
    INNER JOIN dbo.Organizations AS organization
        ON organization.Id = insertedUser.OrganizationId
    WHERE insertedUser.FilialId IS NULL
      AND NOT EXISTS (
          SELECT 1
          FROM dbo.OrganizationFilials AS filial
          WHERE filial.OrganizationId = organization.Id
            AND filial.IsDefault = 1);

    UPDATE users
    SET FilialId = filial.Id
    FROM dbo.Users AS users
    INNER JOIN inserted AS insertedUser
        ON insertedUser.Id = users.Id
    INNER JOIN dbo.OrganizationFilials AS filial
        ON filial.OrganizationId = users.OrganizationId
       AND filial.IsDefault = 1
    WHERE users.FilialId IS NULL;

    IF EXISTS (
        SELECT 1
        FROM dbo.Users AS users
        INNER JOIN inserted AS insertedUser
            ON insertedUser.Id = users.Id
        WHERE users.FilialId IS NULL)
    BEGIN
        THROW 51393, ''WOR-385 could not assign the default filial to a newly inserted user.'', 1;
    END;
END;');

EXEC(N'
CREATE OR ALTER TRIGGER dbo.TR_WOR385_JobReports_DefaultFilial
ON dbo.JobReports
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE jobs
    SET FilialId = filial.Id
    FROM dbo.JobReports AS jobs
    INNER JOIN inserted AS insertedJob
        ON insertedJob.Id = jobs.Id
    INNER JOIN dbo.OrganizationFilials AS filial
        ON filial.OrganizationId = jobs.OrganizationId
       AND filial.IsDefault = 1
    WHERE jobs.FilialId IS NULL;

    IF EXISTS (
        SELECT 1
        FROM dbo.JobReports AS jobs
        INNER JOIN inserted AS insertedJob
            ON insertedJob.Id = jobs.Id
        WHERE jobs.FilialId IS NULL)
    BEGIN
        THROW 51394, ''WOR-385 could not assign the default filial to a newly inserted job.'', 1;
    END;
END;');

EXEC(N'
CREATE OR ALTER TRIGGER dbo.TR_WOR385_InstallationCategories_Organization
ON dbo.JobReportInstallationCategories
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE snapshotCategory
    SET OrganizationId = installation.OrganizationId
    FROM dbo.JobReportInstallationCategories AS snapshotCategory
    INNER JOIN inserted AS insertedCategory
        ON insertedCategory.Id = snapshotCategory.Id
    INNER JOIN dbo.JobReportInstallations AS installation
        ON installation.Id = snapshotCategory.JobReportInstallationId
    WHERE snapshotCategory.OrganizationId IS NULL;

    IF EXISTS (
        SELECT 1
        FROM dbo.JobReportInstallationCategories AS snapshotCategory
        INNER JOIN inserted AS insertedCategory
            ON insertedCategory.Id = snapshotCategory.Id
        WHERE snapshotCategory.OrganizationId IS NULL)
    BEGIN
        THROW 51395, ''WOR-385 could not derive OrganizationId for a newly inserted category snapshot.'', 1;
    END;
END;');

EXEC(N'
CREATE OR ALTER TRIGGER dbo.TR_WOR385_InstallationControlPoints_Organization
ON dbo.JobReportInstallationControlPoints
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE snapshotPoint
    SET OrganizationId = snapshotCategory.OrganizationId
    FROM dbo.JobReportInstallationControlPoints AS snapshotPoint
    INNER JOIN inserted AS insertedPoint
        ON insertedPoint.JobReportInstallationCategoryId = snapshotPoint.JobReportInstallationCategoryId
       AND insertedPoint.ControlPointId = snapshotPoint.ControlPointId
    INNER JOIN dbo.JobReportInstallationCategories AS snapshotCategory
        ON snapshotCategory.Id = snapshotPoint.JobReportInstallationCategoryId
    WHERE snapshotPoint.OrganizationId IS NULL;

    IF EXISTS (
        SELECT 1
        FROM dbo.JobReportInstallationControlPoints AS snapshotPoint
        INNER JOIN inserted AS insertedPoint
            ON insertedPoint.JobReportInstallationCategoryId = snapshotPoint.JobReportInstallationCategoryId
           AND insertedPoint.ControlPointId = snapshotPoint.ControlPointId
        WHERE snapshotPoint.OrganizationId IS NULL)
    BEGIN
        THROW 51396, ''WOR-385 could not derive OrganizationId for a newly inserted control-point snapshot.'', 1;
    END;
END;');