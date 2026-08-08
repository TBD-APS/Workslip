-- WOR-160
-- Tenant-scope installation category and control-point snapshots before tightening FKs.

IF OBJECT_ID(N'dbo.JobReportInstallations', N'U') IS NULL
    THROW 51160, 'WOR-160 requires dbo.JobReportInstallations.', 1;
IF OBJECT_ID(N'dbo.JobReportInstallationCategories', N'U') IS NULL
    THROW 51160, 'WOR-160 requires dbo.JobReportInstallationCategories.', 1;
IF OBJECT_ID(N'dbo.JobReportInstallationControlPoints', N'U') IS NULL
    THROW 51160, 'WOR-160 requires dbo.JobReportInstallationControlPoints.', 1;
IF OBJECT_ID(N'dbo.ControlCategories', N'U') IS NULL
    THROW 51160, 'WOR-160 requires dbo.ControlCategories.', 1;
IF OBJECT_ID(N'dbo.ControlPoints', N'U') IS NULL
    THROW 51160, 'WOR-160 requires dbo.ControlPoints.', 1;

DECLARE @invalidCategoryCount int;
DECLARE @invalidControlPointCount int;
DECLARE @errorMessage nvarchar(2048);

SELECT @invalidCategoryCount = COUNT(*)
FROM dbo.JobReportInstallationCategories AS snapshotCategory
INNER JOIN dbo.JobReportInstallations AS installation
    ON installation.Id = snapshotCategory.JobReportInstallationId
INNER JOIN dbo.ControlCategories AS controlCategory
    ON controlCategory.Id = snapshotCategory.ControlCategoryId
WHERE installation.OrganizationId <> controlCategory.OrganizationId;

IF @invalidCategoryCount > 0
BEGIN
    SET @errorMessage = CONCAT(
        'WOR-160 blocked: ',
        @invalidCategoryCount,
        ' installation category snapshot row(s) reference another organization.');
    THROW 51161, @errorMessage, 1;
END;

SELECT @invalidControlPointCount = COUNT(*)
FROM dbo.JobReportInstallationControlPoints AS snapshotPoint
INNER JOIN dbo.JobReportInstallationCategories AS snapshotCategory
    ON snapshotCategory.Id = snapshotPoint.JobReportInstallationCategoryId
INNER JOIN dbo.JobReportInstallations AS installation
    ON installation.Id = snapshotCategory.JobReportInstallationId
INNER JOIN dbo.ControlPoints AS controlPoint
    ON controlPoint.Id = snapshotPoint.ControlPointId
WHERE installation.OrganizationId <> controlPoint.OrganizationId;

IF @invalidControlPointCount > 0
BEGIN
    SET @errorMessage = CONCAT(
        'WOR-160 blocked: ',
        @invalidControlPointCount,
        ' installation control-point snapshot row(s) reference another organization.');
    THROW 51162, @errorMessage, 1;
END;

IF COL_LENGTH(N'dbo.JobReportInstallationCategories', N'OrganizationId') IS NULL
    ALTER TABLE dbo.JobReportInstallationCategories ADD OrganizationId uniqueidentifier NULL;

IF COL_LENGTH(N'dbo.JobReportInstallationControlPoints', N'OrganizationId') IS NULL
    ALTER TABLE dbo.JobReportInstallationControlPoints ADD OrganizationId uniqueidentifier NULL;

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
    THROW 51163, 'WOR-160 could not derive OrganizationId for every category snapshot.', 1;
IF EXISTS (SELECT 1 FROM dbo.JobReportInstallationControlPoints WHERE OrganizationId IS NULL)
    THROW 51164, 'WOR-160 could not derive OrganizationId for every control-point snapshot.', 1;

ALTER TABLE dbo.JobReportInstallationCategories
    ALTER COLUMN OrganizationId uniqueidentifier NOT NULL;
ALTER TABLE dbo.JobReportInstallationControlPoints
    ALTER COLUMN OrganizationId uniqueidentifier NOT NULL;

IF NOT EXISTS (
    SELECT 1
    FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.JobReportInstallations')
      AND name = N'AK_JobReportInstallations_OrganizationId_Id')
BEGIN
    ALTER TABLE dbo.JobReportInstallations
        ADD CONSTRAINT AK_JobReportInstallations_OrganizationId_Id
        UNIQUE (OrganizationId, Id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.ControlCategories')
      AND name = N'AK_ControlCategories_OrganizationId_Id')
BEGIN
    ALTER TABLE dbo.ControlCategories
        ADD CONSTRAINT AK_ControlCategories_OrganizationId_Id
        UNIQUE (OrganizationId, Id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.ControlPoints')
      AND name = N'AK_ControlPoints_OrganizationId_Id')
BEGIN
    ALTER TABLE dbo.ControlPoints
        ADD CONSTRAINT AK_ControlPoints_OrganizationId_Id
        UNIQUE (OrganizationId, Id);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.key_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.JobReportInstallationCategories')
      AND name = N'AK_JobReportInstallationCategories_OrganizationId_Id')
BEGIN
    ALTER TABLE dbo.JobReportInstallationCategories
        ADD CONSTRAINT AK_JobReportInstallationCategories_OrganizationId_Id
        UNIQUE (OrganizationId, Id);
END;

IF OBJECT_ID(N'dbo.FK_JobReportInstallationCategories_JobReportInstallations_JobReportInstallationId', N'F') IS NOT NULL
BEGIN
    ALTER TABLE dbo.JobReportInstallationCategories
        DROP CONSTRAINT FK_JobReportInstallationCategories_JobReportInstallations_JobReportInstallationId;
END;

IF OBJECT_ID(N'dbo.FK_JobReportInstallationCategories_ControlCategories_ControlCategoryId', N'F') IS NOT NULL
BEGIN
    ALTER TABLE dbo.JobReportInstallationCategories
        DROP CONSTRAINT FK_JobReportInstallationCategories_ControlCategories_ControlCategoryId;
END;

IF OBJECT_ID(N'dbo.FK_JobReportInstallationControlPoints_JobReportInstallationCategories_JobReportInstallationCategoryId', N'F') IS NOT NULL
BEGIN
    ALTER TABLE dbo.JobReportInstallationControlPoints
        DROP CONSTRAINT FK_JobReportInstallationControlPoints_JobReportInstallationCategories_JobReportInstallationCategoryId;
END;

IF OBJECT_ID(N'dbo.FK_JobReportInstallationControlPoints_ControlPoints_ControlPointId', N'F') IS NOT NULL
BEGIN
    ALTER TABLE dbo.JobReportInstallationControlPoints
        DROP CONSTRAINT FK_JobReportInstallationControlPoints_ControlPoints_ControlPointId;
END;

ALTER TABLE dbo.JobReportInstallationCategories WITH CHECK
    ADD CONSTRAINT FK_JobReportInstallationCategories_JobReportInstallations_Organization
    FOREIGN KEY (OrganizationId, JobReportInstallationId)
    REFERENCES dbo.JobReportInstallations (OrganizationId, Id)
    ON DELETE CASCADE;

ALTER TABLE dbo.JobReportInstallationCategories WITH CHECK
    ADD CONSTRAINT FK_JobReportInstallationCategories_ControlCategories_Organization
    FOREIGN KEY (OrganizationId, ControlCategoryId)
    REFERENCES dbo.ControlCategories (OrganizationId, Id)
    ON DELETE NO ACTION;

ALTER TABLE dbo.JobReportInstallationControlPoints WITH CHECK
    ADD CONSTRAINT FK_JobReportInstallationControlPoints_Categories_Organization
    FOREIGN KEY (OrganizationId, JobReportInstallationCategoryId)
    REFERENCES dbo.JobReportInstallationCategories (OrganizationId, Id)
    ON DELETE CASCADE;

ALTER TABLE dbo.JobReportInstallationControlPoints WITH CHECK
    ADD CONSTRAINT FK_JobReportInstallationControlPoints_ControlPoints_Organization
    FOREIGN KEY (OrganizationId, ControlPointId)
    REFERENCES dbo.ControlPoints (OrganizationId, Id)
    ON DELETE NO ACTION;

ALTER TABLE dbo.JobReportInstallationCategories
    CHECK CONSTRAINT FK_JobReportInstallationCategories_JobReportInstallations_Organization;
ALTER TABLE dbo.JobReportInstallationCategories
    CHECK CONSTRAINT FK_JobReportInstallationCategories_ControlCategories_Organization;
ALTER TABLE dbo.JobReportInstallationControlPoints
    CHECK CONSTRAINT FK_JobReportInstallationControlPoints_Categories_Organization;
ALTER TABLE dbo.JobReportInstallationControlPoints
    CHECK CONSTRAINT FK_JobReportInstallationControlPoints_ControlPoints_Organization;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.JobReportInstallationCategories')
      AND name = N'UX_JobReportInstallationCategories_Organization_Installation_Category')
BEGIN
    CREATE UNIQUE INDEX UX_JobReportInstallationCategories_Organization_Installation_Category
        ON dbo.JobReportInstallationCategories (OrganizationId, JobReportInstallationId, ControlCategoryId);
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.JobReportInstallationControlPoints')
      AND name = N'IX_JobReportInstallationControlPoints_Organization_Category_SortOrder')
BEGIN
    CREATE INDEX IX_JobReportInstallationControlPoints_Organization_Category_SortOrder
        ON dbo.JobReportInstallationControlPoints (OrganizationId, JobReportInstallationCategoryId, SortOrder);
END;
