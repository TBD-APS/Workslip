DECLARE @JobReportId UNIQUEIDENTIFIER = '9b46baa6-c56d-4107-b274-130b40111ea8';

SELECT
    itd.Name AS [Installation],
    cc.Name AS [Category],
    cp.Name AS [ControlPoint],
    jricp.IsRequired,
    jricp.IsChecked,
    jric.IsIrrelevant,
    jri.SortOrder AS InstallationOrder,
    jric.SortOrder AS CategoryOrder,
    jricp.SortOrder AS ControlPointOrder
FROM dbo.JobReports jr
INNER JOIN dbo.JobReportInstallations jri
    ON jri.JobReportId = jr.Id
    AND jri.OrganizationId = jr.OrganizationId
INNER JOIN dbo.InstallationTypeDefinitions itd
    ON itd.Id = jri.InstallationTypeDefinitionId
    AND itd.OrganizationId = jr.OrganizationId
INNER JOIN dbo.JobReportInstallationCategories jric
    ON jric.JobReportInstallationId = jri.Id
INNER JOIN dbo.ControlCategories cc
    ON cc.Id = jric.ControlCategoryId
    AND cc.OrganizationId = jr.OrganizationId
INNER JOIN dbo.JobReportInstallationControlPoints jricp
    ON jricp.JobReportInstallationCategoryId = jric.Id
INNER JOIN dbo.ControlPoints cp
    ON cp.Id = jricp.ControlPointId
    AND cp.OrganizationId = jr.OrganizationId
WHERE jr.Id = @JobReportId
ORDER BY
    jri.SortOrder,
    jric.SortOrder,
    jricp.SortOrder;