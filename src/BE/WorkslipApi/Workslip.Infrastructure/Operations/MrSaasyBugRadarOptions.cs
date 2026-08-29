namespace Workslip.Infrastructure.Operations;

/// <summary>
/// Configuration for publishing already-sanitized Workslip diagnostics to the MR SAAS'y
/// Control Center activity stream. Credentials are supplied only through deployment
/// configuration and are intentionally never written to logs or checkpoints.
/// </summary>
public sealed class MrSaasyBugRadarOptions
{
    public const string SectionName = "ControlCenter:MrSaasyBugRadar";

    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string ActivityToken { get; set; } = string.Empty;
    public string CloudflareAccessClientId { get; set; } = string.Empty;
    public string CloudflareAccessClientSecret { get; set; } = string.Empty;
    public string AgentId { get; set; } = "workslip-bug-radar";
    public string Environment { get; set; } = "production";
    public int RefreshIntervalMinutes { get; set; } = 15;
    public int ErrorLimit { get; set; } = 50;
}
