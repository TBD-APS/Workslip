create table dbo.Organizations (
    Id uniqueidentifier not null constraint PK_Organizations primary key,
    Name nvarchar(200) not null,
    CreatedAt datetimeoffset not null constraint DF_Organizations_CreatedAt default sysutcdatetime(),
    UpdatedAt datetimeoffset not null constraint DF_Organizations_UpdatedAt default sysutcdatetime()
);

create table dbo.Users (
    Id uniqueidentifier not null constraint PK_Users primary key,
    OrganizationId uniqueidentifier not null,
    DisplayName nvarchar(200) not null,
    Email nvarchar(320) null,
    Phone nvarchar(80) null,
    Role nvarchar(80) not null,
    CreatedAt datetimeoffset not null constraint DF_Users_CreatedAt default sysutcdatetime(),
    UpdatedAt datetimeoffset not null constraint DF_Users_UpdatedAt default sysutcdatetime(),
    constraint FK_Users_Organizations foreign key (OrganizationId) references dbo.Organizations(Id)
);

create table dbo.Customers (
    Id uniqueidentifier not null constraint PK_Customers primary key,
    OrganizationId uniqueidentifier not null,
    Name nvarchar(240) not null,
    Address nvarchar(500) null,
    Email nvarchar(500) null,
    ContactPerson nvarchar(200) null,
    Phone nvarchar(80) null,
    CreatedAt datetimeoffset not null constraint DF_Customers_CreatedAt default sysutcdatetime(),
    UpdatedAt datetimeoffset not null constraint DF_Customers_UpdatedAt default sysutcdatetime(),
    constraint FK_Customers_Organizations foreign key (OrganizationId) references dbo.Organizations(Id)
);

create table dbo.JobReports (
    Id uniqueidentifier not null constraint PK_JobReports primary key,
    OrganizationId uniqueidentifier not null,
    CustomerId uniqueidentifier not null,
    ReportNumber nvarchar(80) not null,
    Status nvarchar(40) not null,
    ReportDate date null,
    TaskDescription nvarchar(max) not null,
    CustomerObservations nvarchar(max) null,
    InstallationTypesJson nvarchar(max) not null constraint DF_JobReports_InstallationTypesJson default '[]',
    WorkKind nvarchar(80) not null,
    CustomWorkKind nvarchar(160) null,
    Remarks nvarchar(max) null,
    ClosureFlagsJson nvarchar(max) not null constraint DF_JobReports_ClosureFlagsJson default '[]',
    PayloadJson nvarchar(max) null,
    CreatedAt datetimeoffset not null,
    UpdatedAt datetimeoffset not null,
    SubmittedAt datetimeoffset null,
    constraint FK_JobReports_Organizations foreign key (OrganizationId) references dbo.Organizations(Id),
    constraint FK_JobReports_CustomerKey foreign key (CustomerId) references dbo.Customers(Id),
    constraint CK_JobReports_Status check (Status in ('Draft', 'Submitted', 'InReview', 'Approved', 'Rejected', 'Archived')),
    constraint CK_JobReports_InstallationTypesJson_IsJson check (isjson(InstallationTypesJson) = 1),
    constraint CK_JobReports_ClosureFlagsJson_IsJson check (isjson(ClosureFlagsJson) = 1),
    constraint CK_JobReports_PayloadJson_IsJson check (PayloadJson is null or isjson(PayloadJson) = 1)
);

create index IX_JobReports_Organization_Status_UpdatedAt
on dbo.JobReports (OrganizationId, Status, UpdatedAt desc);

create table dbo.JobControlChecks (
    Id uniqueidentifier not null constraint PK_JobControlChecks primary key,
    ReportId uniqueidentifier not null,
    StageId nvarchar(100) not null,
    ColumnId nvarchar(100) not null,
    ItemId nvarchar(160) not null,
    Checked bit not null,
    Note nvarchar(max) null,
    CreatedAt datetimeoffset not null,
    UpdatedAt datetimeoffset not null,
    constraint FK_JobControlChecks_JobReports foreign key (ReportId) references dbo.JobReports(Id) on delete cascade
);

create unique index UX_JobControlChecks_Report_Stage_Column_Item
on dbo.JobControlChecks (ReportId, StageId, ColumnId, ItemId);

create table dbo.JobEvents (
    Id uniqueidentifier not null constraint PK_JobEvents primary key,
    ReportId uniqueidentifier not null,
    ActorId uniqueidentifier null,
    EventType nvarchar(80) not null,
    BeforeJson nvarchar(max) null,
    AfterJson nvarchar(max) null,
    CreatedAt datetimeoffset not null,
    constraint FK_JobEvents_JobReports foreign key (ReportId) references dbo.JobReports(Id) on delete cascade,
    constraint FK_JobEvents_Users foreign key (ActorId) references dbo.Users(Id),
    constraint CK_JobEvents_BeforeJson_IsJson check (BeforeJson is null or isjson(BeforeJson) = 1),
    constraint CK_JobEvents_AfterJson_IsJson check (AfterJson is null or isjson(AfterJson) = 1)
);

create index IX_JobEvents_Report_CreatedAt
on dbo.JobEvents (ReportId, CreatedAt desc);
