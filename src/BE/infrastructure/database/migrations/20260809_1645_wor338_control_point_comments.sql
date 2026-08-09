IF COL_LENGTH(N'dbo.JobReportInstallationControlPoints', N'Comment') IS NULL
BEGIN
    ALTER TABLE dbo.JobReportInstallationControlPoints
        ADD Comment nvarchar(max) NULL;
END;
