<?php

namespace App\AI\Context;

use App\Platform\Contracts\TenantContext;
use InvalidArgumentException;

/**
 * The request envelope an agent presents to the Context/Policy Gateway.
 *
 * It carries who is asking ({@see $actingTenant}, {@see $agentId}), what data is
 * being reached for ({@see $targetTenant}, {@see $capability}, {@see $requestedFields})
 * and why ({@see $purpose}, {@see $application}, {@see $environment}). Separating the
 * acting tenant from the target tenant is what lets the gateway refuse cross-tenant
 * access before any data is located.
 */
final readonly class AgentContextRequest
{
    /** @var list<string> */
    public array $requestedFields;

    /** @param list<string> $requestedFields */
    public function __construct(
        public TenantContext $actingTenant,
        public TenantContext $targetTenant,
        public string $capability,
        array $requestedFields,
        public string $agentId,
        public string $purpose,
        public string $application,
        public string $environment,
    ) {
        foreach ([
            'capability' => $capability,
            'agentId' => $agentId,
            'purpose' => $purpose,
            'application' => $application,
            'environment' => $environment,
        ] as $label => $value) {
            if (trim($value) === '') {
                throw new InvalidArgumentException("Context request '{$label}' is required.");
            }
        }

        $normalized = [];
        foreach ($requestedFields as $field) {
            if (!is_string($field) || trim($field) === '') {
                throw new InvalidArgumentException('Requested fields must be non-empty strings.');
            }

            $field = trim($field);
            if (!in_array($field, $normalized, true)) {
                $normalized[] = $field;
            }
        }

        $this->requestedFields = $normalized;
    }

    public function isCrossTenant(): bool
    {
        return $this->actingTenant->tenantId !== $this->targetTenant->tenantId;
    }
}
