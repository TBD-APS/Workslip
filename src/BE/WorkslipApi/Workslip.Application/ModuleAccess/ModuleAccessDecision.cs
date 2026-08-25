namespace Workslip.Application.ModuleAccess;

/// <summary>
/// Explicit module availability outcome. Only <see cref="ModuleAvailability.Enabled"/>
/// authorizes the module to run; every other state fails closed. Mirrors the
/// platform's fail-closed entitlement contract while staying Workslip-owned.
/// </summary>
public enum ModuleAvailability
{
    Enabled = 0,
    Disabled = 1,
    BlockedDependency = 2,
    UnknownModule = 3,
}

/// <summary>
/// Result of evaluating a tenant's access to a single Workslip module. The tenant
/// and user are resolved ambiently from the current request context, so callers
/// only supply the module key.
/// </summary>
public sealed record ModuleAccessDecision(
    WorkslipModuleKey Module,
    ModuleAvailability State,
    string? Reason = null)
{
    public bool IsEnabled => State == ModuleAvailability.Enabled;

    public static ModuleAccessDecision Enabled(WorkslipModuleKey module) =>
        new(module, ModuleAvailability.Enabled);

    public static ModuleAccessDecision Denied(
        WorkslipModuleKey module,
        ModuleAvailability state,
        string? reason = null) => new(module, state, reason);
}
