namespace Workslip.Infrastructure.Reporting;

public sealed class PowerBiExportOptions
{
    public const string SectionName = "PowerBiExport";

    public bool Enabled { get; set; }
    public string ReaderEmail { get; set; } = string.Empty;
    public string ReaderEntraObjectId { get; set; } = string.Empty;
    public string ContainerName { get; set; } = "powerbi-disabled";
    public int HistoryMonths { get; set; } = 24;
    public int RefreshIntervalMinutes { get; set; } = 60;
}
