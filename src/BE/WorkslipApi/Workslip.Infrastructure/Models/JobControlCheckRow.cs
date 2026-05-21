public sealed class JobControlCheckRow
{
    public Guid Id { get; init; }
    public string StageId { get; init; } = string.Empty;
    public string ColumnId { get; init; } = string.Empty;
    public string ItemId { get; init; } = string.Empty;
    public bool Checked { get; init; }
    public string? Note { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}