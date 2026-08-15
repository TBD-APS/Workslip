SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
    THROW 51420, 'WOR-412 legacy test-user audience repair requires dbo.Users.', 1;

IF COL_LENGTH(N'dbo.Users', N'UserKind') IS NULL
    THROW 51421, 'WOR-412 legacy test-user audience repair requires dbo.Users.UserKind.', 1;

IF COL_LENGTH(N'dbo.Users', N'Email') IS NULL
    THROW 51422, 'WOR-412 legacy test-user audience repair requires dbo.Users.Email.', 1;

IF COL_LENGTH(N'dbo.Users', N'DisplayName') IS NULL
    THROW 51423, 'WOR-412 legacy test-user audience repair requires dbo.Users.DisplayName.', 1;

IF COL_LENGTH(N'dbo.Users', N'Role') IS NULL
    THROW 51424, 'WOR-412 legacy test-user audience repair requires dbo.Users.Role.', 1;

IF COL_LENGTH(N'dbo.Users', N'UpdatedAt') IS NULL
    THROW 51425, 'WOR-412 legacy test-user audience repair requires dbo.Users.UpdatedAt.', 1;

DECLARE @now datetimeoffset(7) = SYSUTCDATETIME();

-- One-time repair for the historical Workslip development identities that were
-- persisted before UserKind existed. Exact identity matching is intentionally
-- limited to this migration; runtime authorization remains UserKind-based and
-- does not depend on these IDs or e-mail addresses.
UPDATE dbo.Users
SET UserKind = N'InternalTest',
    UpdatedAt = @now
WHERE UserKind = N'Member'
  AND (
        (Id = CONVERT(uniqueidentifier, 'A1A1A1A1-DA5B-4CC4-BBEB-07B40CAB806F')
         AND Email = N'admin@17v3ygzs.mailosaur.net'
         AND DisplayName = N'Niels Petersen'
         AND Role = N'Admin')
     OR (Id = CONVERT(uniqueidentifier, 'B2B2B2B2-DA5B-4CC4-BBEB-07B40CAB806F')
         AND Email = N'user@17v3ygzs.mailosaur.net'
         AND DisplayName = N'Arne Arnesen'
         AND Role = N'User')
     OR (Id = CONVERT(uniqueidentifier, 'C3C3C3C3-DA5B-4CC4-BBEB-07B40CAB806F')
         AND Email = N'auditor@17v3ygzs.mailosaur.net'
         AND DisplayName = N'Auditor Jakobsen'
         AND Role = N'Auditor')
      );

IF EXISTS (
    SELECT 1
    FROM dbo.Users
    WHERE (
            (Id = CONVERT(uniqueidentifier, 'A1A1A1A1-DA5B-4CC4-BBEB-07B40CAB806F')
             AND Email = N'admin@17v3ygzs.mailosaur.net'
             AND DisplayName = N'Niels Petersen'
             AND Role = N'Admin')
         OR (Id = CONVERT(uniqueidentifier, 'B2B2B2B2-DA5B-4CC4-BBEB-07B40CAB806F')
             AND Email = N'user@17v3ygzs.mailosaur.net'
             AND DisplayName = N'Arne Arnesen'
             AND Role = N'User')
         OR (Id = CONVERT(uniqueidentifier, 'C3C3C3C3-DA5B-4CC4-BBEB-07B40CAB806F')
             AND Email = N'auditor@17v3ygzs.mailosaur.net'
             AND DisplayName = N'Auditor Jakobsen'
             AND Role = N'Auditor')
          )
      AND UserKind <> N'InternalTest'
)
    THROW 51426, 'WOR-412 could not reconcile an exact legacy test identity to InternalTest.', 1;
