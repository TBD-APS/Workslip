-- Fix missing Reopened status in JobReports check constraint.
-- Production databases created before this migration still reject Approved -> Reopened
-- transitions with a CHECK constraint violation even though the domain and API allow it.

IF OBJECT_ID(N'dbo.JobReports', N'U') IS NOT NULL
BEGIN
    IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_JobReports_Status')
        ALTER TABLE [dbo].[JobReports] DROP CONSTRAINT [CK_JobReports_Status];

    ALTER TABLE [dbo].[JobReports] ADD CONSTRAINT [CK_JobReports_Status] CHECK (Status in ('Draft', 'InReview', 'Approved', 'Rejected', 'Reopened'));
END
