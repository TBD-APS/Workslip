SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.InventoryMaterials', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.InventoryMaterials
    (
        Id uniqueidentifier NOT NULL,
        OrganizationId uniqueidentifier NOT NULL,
        Name nvarchar(120) NOT NULL,
        Sku nvarchar(64) NOT NULL,
        Unit nvarchar(24) NOT NULL,
        UnitCost decimal(18,2) NOT NULL CONSTRAINT DF_InventoryMaterials_UnitCost DEFAULT 0,
        IsActive bit NOT NULL CONSTRAINT DF_InventoryMaterials_IsActive DEFAULT 1,
        QrCode uniqueidentifier NOT NULL,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_InventoryMaterials_CreatedAt DEFAULT sysutcdatetime(),
        CONSTRAINT PK_InventoryMaterials PRIMARY KEY (Id),
        CONSTRAINT UQ_InventoryMaterials_Organization_Id UNIQUE (OrganizationId, Id),
        CONSTRAINT UQ_InventoryMaterials_Organization_Sku UNIQUE (OrganizationId, Sku),
        CONSTRAINT UQ_InventoryMaterials_Organization_QrCode UNIQUE (OrganizationId, QrCode),
        CONSTRAINT FK_InventoryMaterials_Organizations_OrganizationId
            FOREIGN KEY (OrganizationId) REFERENCES dbo.Organizations(Id),
        CONSTRAINT CK_InventoryMaterials_Name_NotBlank CHECK (LEN(LTRIM(RTRIM(Name))) > 0),
        CONSTRAINT CK_InventoryMaterials_Sku_NotBlank CHECK (LEN(LTRIM(RTRIM(Sku))) > 0),
        CONSTRAINT CK_InventoryMaterials_Unit_NotBlank CHECK (LEN(LTRIM(RTRIM(Unit))) > 0),
        CONSTRAINT CK_InventoryMaterials_UnitCost_NonNegative CHECK (UnitCost >= 0)
    );

    CREATE INDEX IX_InventoryMaterials_Organization_Name
        ON dbo.InventoryMaterials (OrganizationId, IsActive, Name, Id);
END;

IF OBJECT_ID(N'dbo.InventoryLocations', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.InventoryLocations
    (
        Id uniqueidentifier NOT NULL,
        OrganizationId uniqueidentifier NOT NULL,
        Name nvarchar(100) NOT NULL,
        IsActive bit NOT NULL CONSTRAINT DF_InventoryLocations_IsActive DEFAULT 1,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_InventoryLocations_CreatedAt DEFAULT sysutcdatetime(),
        CONSTRAINT PK_InventoryLocations PRIMARY KEY (Id),
        CONSTRAINT UQ_InventoryLocations_Organization_Id UNIQUE (OrganizationId, Id),
        CONSTRAINT UQ_InventoryLocations_Organization_Name UNIQUE (OrganizationId, Name),
        CONSTRAINT FK_InventoryLocations_Organizations_OrganizationId
            FOREIGN KEY (OrganizationId) REFERENCES dbo.Organizations(Id),
        CONSTRAINT CK_InventoryLocations_Name_NotBlank CHECK (LEN(LTRIM(RTRIM(Name))) > 0)
    );

    CREATE INDEX IX_InventoryLocations_Organization_Active_Name
        ON dbo.InventoryLocations (OrganizationId, IsActive, Name, Id);
END;

IF OBJECT_ID(N'dbo.InventoryBalances', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.InventoryBalances
    (
        OrganizationId uniqueidentifier NOT NULL,
        MaterialId uniqueidentifier NOT NULL,
        LocationId uniqueidentifier NOT NULL,
        Quantity decimal(18,3) NOT NULL CONSTRAINT DF_InventoryBalances_Quantity DEFAULT 0,
        UpdatedAt datetimeoffset NOT NULL CONSTRAINT DF_InventoryBalances_UpdatedAt DEFAULT sysutcdatetime(),
        CONSTRAINT PK_InventoryBalances PRIMARY KEY (OrganizationId, MaterialId, LocationId),
        CONSTRAINT FK_InventoryBalances_Material
            FOREIGN KEY (OrganizationId, MaterialId)
            REFERENCES dbo.InventoryMaterials(OrganizationId, Id),
        CONSTRAINT FK_InventoryBalances_Location
            FOREIGN KEY (OrganizationId, LocationId)
            REFERENCES dbo.InventoryLocations(OrganizationId, Id),
        CONSTRAINT CK_InventoryBalances_Quantity_NonNegative CHECK (Quantity >= 0)
    );

    CREATE INDEX IX_InventoryBalances_Organization_Location
        ON dbo.InventoryBalances (OrganizationId, LocationId, MaterialId);
END;

IF OBJECT_ID(N'dbo.InventoryMovements', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.InventoryMovements
    (
        Id uniqueidentifier NOT NULL,
        OrganizationId uniqueidentifier NOT NULL,
        MaterialId uniqueidentifier NOT NULL,
        LocationId uniqueidentifier NOT NULL,
        MovementType nvarchar(16) NOT NULL,
        QuantityChange decimal(18,3) NOT NULL,
        BalanceAfter decimal(18,3) NOT NULL,
        CommandId uniqueidentifier NOT NULL,
        ActorUserId uniqueidentifier NULL,
        Reason nvarchar(200) NULL,
        CreatedAt datetimeoffset NOT NULL CONSTRAINT DF_InventoryMovements_CreatedAt DEFAULT sysutcdatetime(),
        CONSTRAINT PK_InventoryMovements PRIMARY KEY (Id),
        CONSTRAINT UQ_InventoryMovements_Organization_CommandId UNIQUE (OrganizationId, CommandId),
        CONSTRAINT FK_InventoryMovements_Material
            FOREIGN KEY (OrganizationId, MaterialId)
            REFERENCES dbo.InventoryMaterials(OrganizationId, Id),
        CONSTRAINT FK_InventoryMovements_Location
            FOREIGN KEY (OrganizationId, LocationId)
            REFERENCES dbo.InventoryLocations(OrganizationId, Id),
        CONSTRAINT CK_InventoryMovements_Type CHECK (MovementType IN (N'in', N'out')),
        CONSTRAINT CK_InventoryMovements_QuantityChange_NotZero CHECK (QuantityChange <> 0),
        CONSTRAINT CK_InventoryMovements_BalanceAfter_NonNegative CHECK (BalanceAfter >= 0)
    );

    CREATE INDEX IX_InventoryMovements_Organization_CreatedAt
        ON dbo.InventoryMovements (OrganizationId, CreatedAt DESC, Id);

    CREATE INDEX IX_InventoryMovements_Organization_Material_CreatedAt
        ON dbo.InventoryMovements (OrganizationId, MaterialId, CreatedAt DESC, Id);
END;
