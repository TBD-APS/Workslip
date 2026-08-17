<?php

namespace App\AI\Context;

/**
 * Machine-readable reason a Context/Policy Gateway request was denied.
 *
 * Values are stable identifiers safe to record in audit evidence and surface in
 * a denied-access UI; they never carry customer payload.
 */
enum ContextDenialReason: string
{
    case CrossTenantAccess = 'cross_tenant_access';
    case UnknownCapability = 'unknown_capability';
    case CapabilityForbidden = 'capability_forbidden';
    case NoPermittedFields = 'no_permitted_fields';
}
