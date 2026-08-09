-- WOR-398
-- Complete the WOR-385 expand/contract rollout now that the Filial-aware API owns all writes.
-- Tighten ownership columns to NOT NULL, keep tenant FKs trusted, and remove the temporary rollout triggers.

IF OBJECT_ID(N'dbo.OrganizationFilials', N'U') IS NULL
    THROW 51398, 'WOR-398 requires dbo.OrganizationFilials.', 1;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
    THROW 51398, 'WOR-398 requires dbo.Users.', 1;
IF OBJECT_ID(N'dbo.JobReports', N'U') IS NULL
    THROW 51398, 'WOR-398 requires dbo.JobReports.', 1;
IF OBJECT_ID(N'dbo.JobReportInstallations', N'U') IS NULL
    THROW 51398, 'WOR-398 requires dbo.JobReportInstallations.', 1;
IF OBJECT_ID(N'dbo.JobReportInstallationCategories', N'U') IS NULL
    THROW 51398, 'WOR-398 requires dbo.JobReportInstallationCategories.', 1;
IF OBJECT_ID(N'dbo.JobReportInstallationControlPoints', N'U') IS NULL
    THROW 51398, 'WOR-398 requires dbo.JobReportInstallationControlPoints.', 1;
IF OBJECT_ID(N'dbo.ControlCategories', N'U') IS NULL
    THROW 51398, 'WOR-398 requires dbo.ControlCategories.', 1;
IF OBJECT_ID(N'dbo.ControlPoints', N'U') IS NULL
    THROW 51398, 'WOR-398 requires dbo.ControlPoints.', 1;

IF COL_LENGTH(N'dbo.Users', N'FilialId') IS NULL
    THROW 51398, 'WOR-398 requires dbo.Users.FilialId from WOR-385.', 1;
IF COL_LENGTH(N'dbo.JobReports', N'FilialId') IS NULL
    THROW 51398, 'WOR-398 requires dbo.JobReports.FilialId from WOR-385.', 1;
IF COL_LENGTH(N'dbo.JobReportInstallationCategories', N'OrganizationId') IS NULL
    THROW 51398, 'WOR-398 requires dbo.JobReportInstallationCategories.OrganizationId from WOR-385.', 1;
IF COL_LENGTH(N'dbo.JobReportInstallationControlPoints', N'OrganizationId') IS NULL
    THROW 51398, 'WOR-398 requires dbo.JobReportInstallationControlPoints.OrganizationId from WOR-385.', 1;

IF OBJECT_ID(N'dbo.FK_Users_OrganizationFilials_OrganizationId_FilialId', N'F') IS NULL
    THROW 51398, 'WOR-398 requires the Users-to-Filial tenant FK from WOR-385.', 1;
IF OBJECT_ID(N'dbo.FK_JobReports_OrganizationFilials_OrganizationId_FilialId', N'F') IS NULL
    THROW 51398, 'WOR-398 requires the JobReports-to-Filial tenant FK from WOR-385.', 1;
IF OBJECT_ID(N'dbo.FK_JobReportInstallationCategories_JobReportInstallations_Organization', N'F') IS NULL
    THROW 51398, 'WOR-398 requires the category-to-installation tenant FK from WOR-385.', 1;
IF OBJECT_ID(N'dbo.FK_JobReportInstallationCategories_ControlCategories_Organization', N'F') IS NULL
    THROW 51398, 'WOR-398 requires the category-to-definition tenant FK from WOR-385.', 1;
IF OBJECT_ID(N'dbo.FK_JobReportInstallationControlPoints_Categories_Organization', N'F') IS NULL
    THROW 51398, 'WOR-398 requires the control-point-to-category tenant FK from WOR-385.', 1;
IF OBJECT_ID(N'dbo.FK_JobReportInstallationControlPoints_ControlPoints_Organization', N'F') IS NULL
    THROW 51398, 'WOR-398 requires the control-point-to-definition tenant FK from WOR-385.', 1;

-- Fail closed on non-null ownership that cannot be repaired deterministically.
IF EXISTS (
    SELECT 1
    FROM dbo.Users AS users
    LEFT JOIN dbo.OrganizationFilials AS filial
        ON filial.OrganizationId = users.OrganizationId
       AND filial.Id = users.FilialId
    WHERE users.FilialId IS NOT NULL
      AND filial.Id IS NULL)
BEGIN
    THROW 51399, 'WOR-398 blocked: Users contains an invalid cross-organization FilialId.', 1;
END;

IF EXISTS (
    SELECT 1
    FROM dbo.JobReports AS jobs
    LEFT JOIN dbo.OrganizationFilials AS filial
        ON filial.OrganizationId = jobs.OrganizationId
       AND filial.Id = jobs.FilialId
    WHERE jobs.FilialId IS NOT NULL
      AND filial.Id IS NULL)
BEGIN
    THROW 51400, 'WOR-398 blocked: JobReports contains an invalid cross-organization FilialId.', 1;
END;

IF EXISTS (
    SELECT 1
    FROM dbo.JobReportInstallationCategories AS category
    LEFT JOIN dbo.JobReportInstallations AS installation
        ON installation.Id = category.JobReportInstallationId
    LEFT JOIN dbo.ControlCategories AS definition
        ON definition.Id = category.ControlCategoryId
    WHERE installation.Id IS NULL
       OR definition.Id IS NULL
       OR installation.OrganizationId <> definition.OrganizationId
       OR (category.OrganizationId IS NOT NULL AND category.OrganizationId <> installation.OrganizationId))
BEGIN
    THROW 51401, 'WOR-398 blocked: installation category ownership is invalid.', 1;
END;

-- Defensive backfill for rows written during the rollout window.
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

UPDATE category
SET OrganizationId = installation.OrganizationId
FROM dbo.JobReportInstallationCategories AS category
INNER JOIN dbo.JobReportInstallations AS installation
    ON installation.Id = category.JobReportInstallationId
WHERE category.OrganizationId IS NULL;

IF EXISTS (
    SELECT 1
    FROM dbo.JobReportInstallationControlPoints AS point
    LEFT JOIN dbo.JobReportInstallationCategories AS category
        ON category.Id = point.JobReportInstallationCategoryId
    LEFT JOIN dbo.ControlPoints AS definition
        ON definition.Id = point.ControlPointId
    WHERE category.Id IS NULL
       OR definition.Id IS NULL
       OR category.OrganizationId IS NULL
       OR category.OrganizationId <> definition.OrganizationId
       OR (point.OrganizationId IS NOT NULL AND point.OrganizationId <> category.OrganizationId))
BEGIN
    THROW 51402, 'WOR-398 blocked: installation control-point ownership is invalid.', 1;
END;

UPDATE point
SET OrganizationId = category.OrganizationId
FROM dbo.JobReportInstallationControlPoints AS point
INNER JOIN dbo.JobReportInstallationCategories AS category
    ON category.Id = point.JobReportInstallationCategoryId
WHERE point.OrganizationId IS NULL;

-- Contract preflight: no nullable or invalid ownership may remain before schema mutation.
IF EXISTS (
    SELECT 1
    FROM dbo.Users AS users
    LEFT JOIN dbo.OrganizationFilials AS filial
        ON filial.OrganizationId = users.OrganizationId
       AND filial.Id = users.FilialId
    WHERE users.FilialId IS NULL OR filial.Id IS NULL)
BEGIN
    THROW 51403, 'WOR-398 blocked: every user must belong to a Filial in its Organization.', 1;
END;

IF EXISTS (
    SELECT 1
    FROM dbo.JobReports AS jobs
    LEFT JOIN dbo.OrganizationFilials AS filial
        ON filial.OrganizationId = jobs.OrganizationId
       AND filial.Id = jobs.FilialId
    WHERE jobs.FilialId IS NULL OR filial.Id IS NULL)
BEGIN
    THROW 51404, 'WOR-398 blocked: every job must belong to a Filial in its Organization.', 1;
END;

IF EXISTS (
    SELECT 1
    FROM dbo.JobReportInstallationCategories AS category
    INNER JOIN dbo.JobReportInstallations AS installation
        ON installation.Id = category.JobReportInstallationId
    INNER JOIN dbo.ControlCategories AS definition
        ON definition.Id = category.ControlCategoryId
    WHERE category.OrganizationId IS NULL
       OR category.OrganizationId <> installation.OrganizationId
       OR category.OrganizationId <> definition.OrganizationId)
BEGIN
    THROW 51405, 'WOR-398 blocked: every installation category must have valid tenant ownership.', 1;
END;

IF EXISTS (
    SELECT 1
    FROM dbo.JobReportInstallationControlPoints AS point
    INNER JOIN dbo.JobReportInstallationCategories AS category
        ON category.Id = point.JobReportInstallationCategoryId
    INNER JOIN dbo.ControlPoints AS definition
        ON definition.Id = point.ControlPointId
    WHERE point.OrganizationId IS NULL
       OR point.OrganizationId <> category.OrganizationId
       OR point.OrganizationId <> definition.OrganizationId)
BEGIN
    THROW 51406, 'WOR-398 blocked: every installation control point must have valid tenant ownership.', 1;
END;

ALTER TABLE dbo.Users WITH CHECK CHECK CONSTRAINT FK_Users_OrganizationFilials_OrganizationId_FilialId;
ALTER TABLE dbo.JobReports WITH CHECK CHECK CONSTRAINT FK_JobReports_OrganizationFilials_OrganizationId_FilialId;
ALTER TABLE dbo.JobReportInstallationCategories WITH CHECK CHECK CONSTRAINT FK_JobReportInstallationCategories_JobReportInstallations_Organization;
ALTER TABLE dbo.JobReportInstallationCategories WITH CHECK CHECK CONSTRAINT FK_JobReportInstallationCategories_ControlCategories_Organization;
ALTER TABLE dbo.JobReportInstallationControlPoints WITH CHECK CHECK CONSTRAINT FK_JobReportInstallationControlPoints_Categories_Organization;
ALTER TABLE dbo.JobReportInstallationControlPoints WITH CHECK CHECK CONSTRAINT FK_JobReportInstallationControlPoints_ControlPoints_Organization;

IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Users')
      AND name = N'FilialId'
      AND is_nullable = 1)
BEGIN
    ALTER TABLE dbo.Users ALTER COLUMN FilialId uniqueidentifier NOT NULL;
END;

IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.JobReports')
      AND name = N'FilialId'
      AND is_nullable = 1)
BEGIN
    ALTER TABLE dbo.JobReports ALTER COLUMN FilialId uniqueidentifier NOT NULL;
END;

IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.JobReportInstallationCategories')
      AND name = N'OrganizationId'
      AND is_nullable = 1)
BEGIN
    ALTER TABLE dbo.JobReportInstallationCategories ALTER COLUMN OrganizationId uniqueidentifier NOT NULL;
END;

IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.JobReportInstallationControlPoints')
      AND name = N'OrganizationId'
      AND is_nullable = 1)
BEGIN
    ALTER TABLE dbo.JobReportInstallationControlPoints ALTER COLUMN OrganizationId uniqueidentifier NOT NULL;
END;

-- The Filial-aware application now supplies ownership itself; rollout compatibility is no longer needed.
IF OBJECT_ID(N'dbo.TR_WOR385_Users_DefaultFilial', N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_WOR385_Users_DefaultFilial;
IF OBJECT_ID(N'dbo.TR_WOR385_JobReports_DefaultFilial', N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_WOR385_JobReports_DefaultFilial;
IF OBJECT_ID(N'dbo.TR_WOR385_InstallationCategories_Organization', N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_WOR385_InstallationCategories_Organization;
IF OBJECT_ID(N'dbo.TR_WOR385_InstallationControlPoints_Organization', N'TR') IS NOT NULL
    DROP TRIGGER dbo.TR_WOR385_InstallationControlPoints_Organization;

IF EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name IN (
        N'FK_Users_OrganizationFilials_OrganizationId_FilialId',
        N'FK_JobReports_OrganizationFilials_OrganizationId_FilialId',
        N'FK_JobReportInstallationCategories_JobReportInstallations_Organization',
        N'FK_JobReportInstallationCategories_ControlCategories_Organization',
        N'FK_JobReportInstallationControlPoints_Categories_Organization',
        N'FK_JobReportInstallationControlPoints_ControlPoints_Organization')
      AND (is_disabled = 1 OR is_not_trusted = 1))
BEGIN
    THROW 51407, 'WOR-398 failed: one or more tenant foreign keys are disabled or untrusted.', 1;
END;

IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE (object_id = OBJECT_ID(N'dbo.Users') AND name = N'FilialId' AND is_nullable = 1)
       OR (object_id = OBJECT_ID(N'dbo.JobReports') AND name = N'FilialId' AND is_nullable = 1)
       OR (object_id = OBJECT_ID(N'dbo.JobReportInstallationCategories') AND name = N'OrganizationId' AND is_nullable = 1)
       OR (object_id = OBJECT_ID(N'dbo.JobReportInstallationControlPoints') AND name = N'OrganizationId' AND is_nullable = 1))
BEGIN
    THROW 51408, 'WOR-398 failed: ownership columns are still nullable.', 1;
END;

IF OBJECT_ID(N'dbo.TR_WOR385_Users_DefaultFilial', N'TR') IS NOT NULL
   OR OBJECT_ID(N'dbo.TR_WOR385_JobReports_DefaultFilial', N'TR') IS NOT NULL
   OR OBJECT_ID(N'dbo.TR_WOR385_InstallationCategories_Organization', N'TR') IS NOT NULL
   OR OBJECT_ID(N'dbo.TR_WOR385_InstallationControlPoints_Organization', N'TR') IS NOT NULL
BEGIN
    THROW 51409, 'WOR-398 failed: rollout compatibility triggers still exist.', 1;
END;
