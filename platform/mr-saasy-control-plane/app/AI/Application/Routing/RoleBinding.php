<?php

namespace App\AI\Application\Routing;

use InvalidArgumentException;

final readonly class RoleBinding
{
    /**
     * @param list<Capability> $requiredCapabilities
     * @param list<ToolCapability> $requiredTools
     */
    public function __construct(
        public AgentRole $role,
        public string $primaryTarget,
        public ?string $fallbackTarget,
        public array $requiredCapabilities,
        public array $requiredTools,
        public RolePermissions $permissions,
        public RoutingPreference $preference = RoutingPreference::Balanced,
    ) {
        if (trim($primaryTarget) === '') {
            throw new InvalidArgumentException('Primary execution target is required.');
        }

        if ($fallbackTarget !== null && trim($fallbackTarget) === '') {
            throw new InvalidArgumentException('Fallback execution target must be null or non-empty.');
        }
    }
}
