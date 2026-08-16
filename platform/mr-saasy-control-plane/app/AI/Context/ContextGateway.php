<?php

namespace App\AI\Context;

use App\Platform\Audit\AuditSink;
use App\Platform\Contracts\TenantContext;
use App\Platform\Policy\ContextPolicy;
use App\ProductAdapters\Contracts\ProductContextPort;
use RuntimeException;

final readonly class ContextGateway
{
    public function __construct(
        private ProductContextPort $productContext,
        private ContextPolicy $policy,
        private AuditSink $audit,
    ) {
    }

    /** @param list<string> $fields */
    public function load(TenantContext $tenant, string $capability, array $fields): SanitizedContext
    {
        if (!$this->policy->allows($tenant, $capability)) {
            $this->audit->record('ai.context.denied', [
                'tenantId' => $tenant->tenantId,
                'capability' => $capability,
            ]);

            throw new RuntimeException('AI context access denied by policy.');
        }

        $context = $this->productContext->loadSanitizedContext($tenant, $fields);

        $this->audit->record('ai.context.allowed', [
            'tenantId' => $tenant->tenantId,
            'capability' => $capability,
            'fieldCount' => count($fields),
        ]);

        return new SanitizedContext($context);
    }
}
