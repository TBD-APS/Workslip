namespace Workslip.Application.ModuleAccess;

/// <summary>
/// Interim default implementation of <see cref="IWorkslipModuleAccess"/> that
/// entitles every known module. It preserves today's behaviour (no capability is
/// gated yet) so the contract can land before the entitlement projection exists.
///
/// Replace at the DI seam with the product-owned adapter that reads the local
/// tenant entitlement projection (sourced from the MR SAAS'y module-entitlement
/// contract). Do not add product gating here — this type only exists to keep the
/// seam wired and behaviour unchanged.
/// </summary>
public sealed class AllModulesEnabledAccess : IWorkslipModuleAccess
{
    public ValueTask<ModuleAccessDecision> EvaluateAsync(
        WorkslipModuleKey module,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ModuleAccessDecision.Enabled(module));

    public ValueTask<IReadOnlyCollection<WorkslipModuleKey>> GetEnabledModulesAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyCollection<WorkslipModuleKey>>(WorkslipModuleKey.All);
}
