<?php

namespace App\AI\Application\Routing;

final readonly class RoutingDecision
{
    public function __construct(
        public AgentRole $role,
        public ExecutionTarget $target,
        public bool $usedFallback,
    ) {
    }
}
