<?php

namespace App\AI\Application\Executive;

use App\AI\Application\Routing\AgentRole;
use App\AI\Application\Routing\RunProvenance;
use DateTimeImmutable;
use InvalidArgumentException;

final readonly class ExecutiveRecommendation
{
    /**
     * @param list<string> $affectedReferences
     * @param list<string> $evidenceReferences
     */
    public function __construct(
        public string $id,
        public AgentRole $ownerRole,
        public ExecutiveDecisionClass $decisionClass,
        public string $summary,
        public RunProvenance $run,
        public DateTimeImmutable $createdAt,
        public array $affectedReferences = [],
        public array $evidenceReferences = [],
    ) {
        if (!ExecutiveHierarchy::isExecutive($ownerRole)) {
            throw new InvalidArgumentException('Executive recommendation owner must be an executive role.');
        }

        if (trim($id) === '' || trim($summary) === '') {
            throw new InvalidArgumentException('Executive recommendation id and summary are required.');
        }

        if ($run->role !== $ownerRole) {
            throw new InvalidArgumentException('Executive recommendation run role must match owner role.');
        }
    }

    public function disposition(): ExecutiveDecisionDisposition
    {
        return ExecutiveDecisionAuthority::disposition($this->decisionClass);
    }
}
