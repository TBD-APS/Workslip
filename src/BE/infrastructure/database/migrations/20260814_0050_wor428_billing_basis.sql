SET XACT_ABORT ON;
BEGIN TRANSACTION;

CREATE TABLE dbo.UserBillingRates
(
    OrganizationId uniqueidentifier NOT NULL,
    UserId uniqueidentifier NOT NULL,
    BillableHourlyRate decimal(18,2) NULL,
    UpdatedAt datetimeoffset NOT NULL CONSTRAINT DF_UserBillingRates_UpdatedAt DEFAULT sysutcdatetime(),
    CONSTRAINT PK_UserBillingRates PRIMARY KEY (OrganizationId, UserId),
    CONSTRAINT FK_UserBillingRates_Users FOREIGN KEY (OrganizationId, UserId)
        REFERENCES dbo.Users (OrganizationId, Id),
    CONSTRAINT CK_UserBillingRates_Rate CHECK (
        BillableHourlyRate IS NULL OR (BillableHourlyRate >= 0 AND BillableHourlyRate <= 100000)
    )
);

CREATE TABLE dbo.WorksheetBillingSnapshots
(
    OrganizationId uniqueidentifier NOT NULL,
    WorksheetId uniqueidentifier NOT NULL,
    BillableHourlyRateSnapshot decimal(18,2) NULL,
    CapturedAtUtc datetimeoffset NOT NULL,
    CONSTRAINT PK_WorksheetBillingSnapshots PRIMARY KEY (OrganizationId, WorksheetId),
    CONSTRAINT FK_WorksheetBillingSnapshots_Worksheets FOREIGN KEY (WorksheetId)
        REFERENCES dbo.Worksheets (Id),
    CONSTRAINT CK_WorksheetBillingSnapshots_Rate CHECK (
        BillableHourlyRateSnapshot IS NULL OR (BillableHourlyRateSnapshot >= 0 AND BillableHourlyRateSnapshot <= 100000)
    )
);

COMMIT TRANSACTION;
