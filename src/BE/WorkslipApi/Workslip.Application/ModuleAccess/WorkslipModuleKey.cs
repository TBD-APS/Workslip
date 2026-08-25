namespace Workslip.Application.ModuleAccess;

/// <summary>
/// Stable, Workslip-owned key for a customer-visible product module (a capability
/// that can be entitled on/off per tenant). These are product-facing keys, not
/// .NET types, routes, or platform identifiers. The opaque MR SAAS'y
/// <c>ModuleId</c> values are mapped onto these keys inside the product-owned
/// entitlement adapter, so the domain never depends on platform identity.
///
/// See ADR 0015 and Docs/architecture/workslip-modular-product-blueprint.md.
/// </summary>
public readonly record struct WorkslipModuleKey(string Value)
{
    public override string ToString() => Value;

    /// <summary>Workspace, identity, tenant isolation, roles, audit, files. Always on; never sold or disabled separately.</summary>
    public static readonly WorkslipModuleKey Foundation = new("foundation");

    /// <summary>Create, assign, execute and close a job for a customer.</summary>
    public static readonly WorkslipModuleKey WorkManagement = new("work-management");

    /// <summary>Register time and see a job's effort and internal economics.</summary>
    public static readonly WorkslipModuleKey TimeEconomics = new("time-economics");

    /// <summary>KLS/job evidence, controlled Docs, review/approval, auditor view, report/PDF output.</summary>
    public static readonly WorkslipModuleKey ComplianceEvidence = new("compliance-evidence");

    /// <summary>Job images, conversation and targeted notifications.</summary>
    public static readonly WorkslipModuleKey FieldCollaboration = new("field-collaboration");

    /// <summary>Read-only overview, Power BI/CSV/PDF projections from enabled source modules.</summary>
    public static readonly WorkslipModuleKey InsightsExports = new("insights-exports");

    /// <summary>All known product module keys.</summary>
    public static readonly IReadOnlyList<WorkslipModuleKey> All = new[]
    {
        Foundation,
        WorkManagement,
        TimeEconomics,
        ComplianceEvidence,
        FieldCollaboration,
        InsightsExports,
    };

    /// <summary>Modules that must never be disabled — the non-negotiable Foundation controls.</summary>
    public static bool IsAlwaysOn(WorkslipModuleKey key) => key == Foundation;
}
