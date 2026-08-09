namespace Workslip.Domain.Models;

public sealed class InventoryMaterialRow
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal UnitCost { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class InventoryLocationRow
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public sealed class InventoryBalanceRow
{
    public Guid OrganizationId { get; init; }
    public Guid MaterialId { get; init; }
    public Guid LocationId { get; init; }
    public decimal Quantity { get; set; }
}

public sealed class JobMaterialRow
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid JobId { get; init; }
    public Guid MaterialId { get; init; }
    public Guid LocationId { get; init; }
    public decimal Quantity { get; set; }
    public decimal PostedQuantity { get; set; }
    public string? MaterialNameSnapshot { get; set; }
    public string? UnitSnapshot { get; set; }
    public decimal? UnitCostSnapshot { get; set; }
    public Guid? PostingBatchId { get; set; }
}

public sealed class InventoryMovementRow
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid MaterialId { get; init; }
    public Guid LocationId { get; init; }
    public Guid JobId { get; init; }
    public Guid JobMaterialId { get; init; }
    public Guid PostingBatchId { get; init; }
    public decimal Quantity { get; init; }
    public string MaterialNameSnapshot { get; init; } = "";
    public string UnitSnapshot { get; init; } = "";
    public decimal UnitCostSnapshot { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
