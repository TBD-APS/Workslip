namespace Workslip.Application.ModuleAccess;

/// <summary>
/// Server-side authority for tenant module entitlement — the backend gate that
/// sits alongside role/permission authorization. Effective access is the
/// intersection of tenant entitlement (this), release state, user role/permission
/// and tenant/data scope (see ADR 0015).
///
/// Implementations resolve the current tenant from the ambient request context.
/// The default implementation entitles every module; a later product-owned
/// adapter backs this with the local entitlement projection sourced from the
/// MR SAAS'y module-entitlement contract, with last-known-good caching and an
/// explicit degraded-mode policy so a platform outage cannot black out the app.
///
/// This is the enforcement authority: navigation/UX gating in the frontend is
/// convenience only and must never be the sole check.
/// </summary>
public interface IWorkslipModuleAccess
{
    /// <summary>Evaluate whether the current tenant is entitled to <paramref name="module"/>.</summary>
    ValueTask<ModuleAccessDecision> EvaluateAsync(
        WorkslipModuleKey module,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The current tenant's effective set of entitled modules. Backs the
    /// read-only effective-capability summary the frontend consumes for
    /// navigation and onboarding.
    /// </summary>
    ValueTask<IReadOnlyCollection<WorkslipModuleKey>> GetEnabledModulesAsync(
        CancellationToken cancellationToken = default);
}
