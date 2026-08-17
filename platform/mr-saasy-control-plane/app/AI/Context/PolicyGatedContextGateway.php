<?php

namespace App\AI\Context;

use App\Platform\Audit\AuditSink;
use App\Platform\Policy\ContextPolicy;
use App\ProductAdapters\Contracts\ProductContextPort;

/**
 * Fail-closed Context/Policy Gateway (WOR-574).
 *
 * The single boundary an AI agent crosses to read product context. It never
 * reaches persistence directly: product data enters only through the
 * {@see ProductContextPort} and only after every check has passed. Any denied
 * decision throws {@see ContextAccessDenied} before customer data is loaded, and
 * both the request and its decision are audited with metadata only — never
 * payload values.
 *
 * Decision order is deliberate — the cheapest, most isolating checks run first so
 * a forbidden request never causes product data to be located:
 *   1. cross-tenant isolation
 *   2. unknown capability
 *   3. tenant + capability authorization
 *   4. field minimization (requested ∩ permitted)
 *   5. load scoped fields through the product port
 *   6. drop any ungranted key returned by the port (defense in depth)
 *   7. apply masking hooks
 */
final readonly class PolicyGatedContextGateway implements ContextGateway
{
    private const string MASK_TOKEN = '***masked***';

    public function __construct(
        private ContextPolicy $authorization,
        private CapabilityContextPolicy $capabilityPolicy,
        private ProductContextPort $productContext,
        private AuditSink $audit,
    ) {
    }

    public function load(AgentContextRequest $request): SanitizedContext
    {
        $this->audit->record('context.request', [
            'acting_tenant' => $request->actingTenant->tenantId,
            'target_tenant' => $request->targetTenant->tenantId,
            'capability' => $request->capability,
            'agent' => $request->agentId,
            'purpose' => $request->purpose,
            'application' => $request->application,
            'environment' => $request->environment,
            'requested_fields' => implode(',', $request->requestedFields),
            'requested_field_count' => count($request->requestedFields),
        ]);

        if ($request->isCrossTenant()) {
            throw $this->deny($request, ContextDenialReason::CrossTenantAccess, [], []);
        }

        if (!$this->capabilityPolicy->knows($request->capability)) {
            throw $this->deny($request, ContextDenialReason::UnknownCapability, [], []);
        }

        if (!$this->authorization->allows($request->actingTenant, $request->capability)) {
            throw $this->deny($request, ContextDenialReason::CapabilityForbidden, [], []);
        }

        $granted = array_values(array_intersect(
            $request->requestedFields,
            $this->capabilityPolicy->permittedFields($request->capability),
        ));

        if ($granted === []) {
            throw $this->deny($request, ContextDenialReason::NoPermittedFields, [], []);
        }

        $raw = $this->productContext->loadSanitizedContext($request->targetTenant, $granted);

        $scoped = [];
        foreach ($granted as $field) {
            $scoped[$field] = $raw[$field] ?? null;
        }

        $maskedFields = array_values(array_intersect(
            $granted,
            $this->capabilityPolicy->maskedFields($request->capability),
        ));
        foreach ($maskedFields as $field) {
            if ($scoped[$field] !== null) {
                $scoped[$field] = self::MASK_TOKEN;
            }
        }

        $this->audit->record('context.decision', [
            'capability' => $request->capability,
            'decision' => 'ALLOWED',
            'reason' => null,
            'granted_fields' => implode(',', $granted),
            'masked_fields' => implode(',', $maskedFields),
        ]);

        return new SanitizedContext($scoped);
    }

    /**
     * @param list<string> $granted
     * @param list<string> $maskedFields
     */
    private function deny(
        AgentContextRequest $request,
        ContextDenialReason $reason,
        array $granted,
        array $maskedFields,
    ): ContextAccessDenied {
        $this->audit->record('context.decision', [
            'capability' => $request->capability,
            'decision' => 'DENIED',
            'reason' => $reason->value,
            'granted_fields' => implode(',', $granted),
            'masked_fields' => implode(',', $maskedFields),
        ]);

        return ContextAccessDenied::because($reason, "capability '{$request->capability}'");
    }
}
