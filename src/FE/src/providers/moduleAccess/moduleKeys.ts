/**
 * Workslip module keys — the customer-visible capabilities that can be
 * entitled (turned on/off) per tenant.
 *
 * These are Workslip-owned, product-facing keys. The platform's opaque
 * `ModuleId` values (from MR SAAS'y) are mapped onto these keys inside the
 * product-owned entitlement adapter, so the rest of the app never depends on
 * platform identifiers directly. See ADR 0015 and the modular-product blueprint.
 *
 * `foundation` is always-on (workspace, identity, tenant isolation, roles,
 * audit, files) and is never sold or switched off separately.
 */

export const MODULE_KEYS = [
  'foundation',
  'work-management',
  'time-economics',
  'compliance-evidence',
  'field-collaboration',
  'insights-exports',
] as const;

export type ModuleKey = (typeof MODULE_KEYS)[number];

/** Modules that must never be disabled — the non-negotiable Foundation controls. */
export const ALWAYS_ON_MODULES: readonly ModuleKey[] = ['foundation'];

export function isModuleKey(value: string): value is ModuleKey {
  return (MODULE_KEYS as readonly string[]).includes(value);
}
