<?php

namespace App\AI\Application\Routing;

use InvalidArgumentException;
use RuntimeException;

final readonly class RoutingDecision
{
    public function __construct(
        public AgentRole $role,
        public ExecutionTarget $target,
        public bool $usedFallback,
    ) {
    }
}

final class RoutingUnavailable extends RuntimeException
{
}

final class RoleRegistry
{
    /** @var array<string, ExecutionTarget> */
    private array $targets = [];

    /** @var array<string, RoleBinding> */
    private array $bindings = [];

    /**
     * @param list<ExecutionTarget> $targets
     * @param list<RoleBinding> $bindings
     */
    public function __construct(array $targets, array $bindings)
    {
        foreach ($targets as $target) {
            if (isset($this->targets[$target->key])) {
                throw new InvalidArgumentException("Duplicate execution target '{$target->key}'.");
            }

            $this->targets[$target->key] = $target;
        }

        foreach ($bindings as $binding) {
            $key = $binding->role->value;
            if (isset($this->bindings[$key])) {
                throw new InvalidArgumentException("Duplicate role binding '{$key}'.");
            }

            $this->bindings[$key] = $binding;
        }
    }

    public function route(AgentRole $role): RoutingDecision
    {
        $binding = $this->bindings[$role->value] ?? null;
        if ($binding === null) {
            throw new RoutingUnavailable("No routing policy configured for role '{$role->value}'.");
        }

        $primary = $this->eligibleTarget($binding->primaryTarget, $binding);
        if ($primary !== null) {
            return new RoutingDecision($role, $primary, false);
        }

        if ($binding->fallbackTarget !== null) {
            $fallback = $this->eligibleTarget($binding->fallbackTarget, $binding);
            if ($fallback !== null) {
                return new RoutingDecision($role, $fallback, true);
            }
        }

        throw new RoutingUnavailable(
            "No configured target satisfies required capabilities/tools for role '{$role->value}'.",
        );
    }

    public function binding(AgentRole $role): RoleBinding
    {
        return $this->bindings[$role->value]
            ?? throw new RoutingUnavailable("No routing policy configured for role '{$role->value}'.");
    }

    private function eligibleTarget(string $key, RoleBinding $binding): ?ExecutionTarget
    {
        $target = $this->targets[$key] ?? null;
        if ($target === null || !$target->enabled) {
            return null;
        }

        if (!$target->supportsCapabilities($binding->requiredCapabilities)) {
            return null;
        }

        if (!$target->supportsTools($binding->requiredTools)) {
            return null;
        }

        return $target;
    }
}
