<?php

namespace App\AI\Application\Routing;

use DateTimeImmutable;
use InvalidArgumentException;

final readonly class RunProvenance
{
    /**
     * @param list<string> $evidenceReferences
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
    }
}

final class SeparationOfDutiesPolicy
{
    public static function canBeSoleApprovingReview(
        RunProvenance $implementation,
        RunProvenance $review,
    ): bool {
        if ($implementation->agentId === $review->agentId) {
            return false;
        }

        return !(
            $implementation->provider === $review->provider
            && $implementation->model === $review->model
        );
    }
}
