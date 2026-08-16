<?php

namespace App\AI\Application\Routing;

use DateTimeImmutable;
use InvalidArgumentException;

final readonly class RunProvenance
{
    /**
     * @param list<string> $evidenceReferences
     *
     * `researchObservedAt` describes when external research/evidence was observed.
     * It is not part of the execution lifecycle and may legitimately predate `startedAt`.
     */
    public function __construct(
        public string $runId,
        public AgentRole $role,
        public string $agentId,
        public string $provider,
        public string $model,
        public DateTimeImmutable $startedAt,
        public ?DateTimeImmutable $completedAt = null,
        public array $evidenceReferences = [],
        public ?DateTimeImmutable $researchObservedAt = null,
    ) {
        foreach ([$runId, $agentId, $provider, $model] as $value) {
            if (trim($value) === '') {
                throw new InvalidArgumentException('Run id, agent id, provider and model are required.');
            }
        }

        if ($completedAt !== null && $completedAt < $startedAt) {
            throw new InvalidArgumentException('Run completion cannot precede run start.');
        }
    }
}
