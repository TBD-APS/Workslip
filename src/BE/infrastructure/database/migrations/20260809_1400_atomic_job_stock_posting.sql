CREATE TABLE dbo.InventoryMaterials (
    Id uniqueidentifier NOT NULL,
    OrganizationId uniqueidentifier NOT NULL,
    Name nvarchar(200) NOT NULL,
    Unit nvarchar(50) NOT NULL,
    UnitCost decimal(18,4) NOT NULL,
    IsActive bit NOT NULL CONSTRAINT DF_InventoryMaterials_IsActive DEFAULT (1),
    CONSTRAINT PK_InventoryMaterials PRIMARY KEY (Id),
    CONSTRAINT UX_InventoryMaterials_Organization_Id UNIQUE (OrganizationId, Id),
    CONSTRAINT FK_InventoryMaterials_Organizations FOREIGN KEY (OrganizationId) REFERENCES dbo.Organizations(Id)
);

CREATE TABLE dbo.InventoryLocations (
    Id uniqueidentifier NOT NULL,
    OrganizationId uniqueidentifier NOT NULL,
    Name nvarchar(200) NOT NULL,
    IsActive bit NOT NULL CONSTRAINT DF_InventoryLocations_IsActive DEFAULT (1),
    CONSTRAINT PK_InventoryLocations PRIMARY KEY (Id),
    CONSTRAINT UX_InventoryLocations_Organization_Id UNIQUE (OrganizationId, Id),
    CONSTRAINT FK_InventoryLocations_Organizations FOREIGN KEY (OrganizationId) REFERENCES dbo.Organizations(Id)
);

CREATE TABLE dbo.InventoryBalances (
    OrganizationId uniqueidentifier NOT NULL,
    MaterialId uniqueidentifier NOT NULL,
    LocationId uniqueidentifier NOT NULL,
    Quantity decimal(18,4) NOT NULL,
    CONSTRAINT PK_InventoryBalances PRIMARY KEY (OrganizationId, MaterialId, LocationId),
    CONSTRAINT CK_InventoryBalances_NonNegative CHECK (Quantity >= 0),
    CONSTRAINT FK_InventoryBalances_Materials FOREIGN KEY (OrganizationId, MaterialId) REFERENCES dbo.InventoryMaterials(OrganizationId, Id),
    CONSTRAINT FK_InventoryBalances_Locations FOREIGN KEY (OrganizationId, LocationId) REFERENCES dbo.InventoryLocations(OrganizationId, Id)
);

CREATE TABLE dbo.JobMaterials (
    Id uniqueidentifier NOT NULL,
    OrganizationId uniqueidentifier NOT NULL,
    JobId uniqueidentifier NOT NULL,
    MaterialId uniqueidentifier NOT NULL,
    LocationId uniqueidentifier NOT NULL,
    Quantity decimal(18,4) NOT NULL,
    PostedQuantity decimal(18,4) NOT NULL CONSTRAINT DF_JobMaterials_Posted DEFAULT (0),
    MaterialNameSnapshot nvarchar(200) NULL,
    UnitSnapshot nvarchar(50) NULL,
    UnitCostSnapshot decimal(18,4) NULL,
    PostingBatchId uniqueidentifier NULL,
    CONSTRAINT PK_JobMaterials PRIMARY KEY (Id),
    CONSTRAINT UX_JobMaterials_Organization_Id UNIQUE (OrganizationId, Id),
    CONSTRAINT CK_JobMaterials_Quantity_Positive CHECK (Quantity > 0),
    CONSTRAINT CK_JobMaterials_PostedQuantity_Valid CHECK (PostedQuantity >= 0 AND PostedQuantity <= Quantity),
    CONSTRAINT UX_JobMaterials_Line UNIQUE (OrganizationId, JobId, MaterialId, LocationId),
    CONSTRAINT FK_JobMaterials_Jobs FOREIGN KEY (OrganizationId, JobId) REFERENCES dbo.JobReports(OrganizationId, Id),
    CONSTRAINT FK_JobMaterials_Materials FOREIGN KEY (OrganizationId, MaterialId) REFERENCES dbo.InventoryMaterials(OrganizationId, Id),
    CONSTRAINT FK_JobMaterials_Locations FOREIGN KEY (OrganizationId, LocationId) REFERENCES dbo.InventoryLocations(OrganizationId, Id)
);

CREATE TABLE dbo.InventoryMovements (
    Id uniqueidentifier NOT NULL,
    OrganizationId uniqueidentifier NOT NULL,
    MaterialId uniqueidentifier NOT NULL,
    LocationId uniqueidentifier NOT NULL,
    JobId uniqueidentifier NOT NULL,
    JobMaterialId uniqueidentifier NOT NULL,
    PostingBatchId uniqueidentifier NOT NULL,
    Quantity decimal(18,4) NOT NULL,
    MaterialNameSnapshot nvarchar(200) NOT NULL,
    UnitSnapshot nvarchar(50) NOT NULL,
    UnitCostSnapshot decimal(18,4) NOT NULL,
    CreatedAt datetimeoffset NOT NULL,
    CONSTRAINT PK_InventoryMovements PRIMARY KEY (Id),
    CONSTRAINT UX_InventoryMovements_Line_Batch UNIQUE (JobMaterialId, PostingBatchId),
    CONSTRAINT FK_InventoryMovements_JobMaterials FOREIGN KEY (OrganizationId, JobMaterialId) REFERENCES dbo.JobMaterials(OrganizationId, Id)
);

CREATE INDEX IX_InventoryMovements_Organization_Batch ON dbo.InventoryMovements(OrganizationId, PostingBatchId);
