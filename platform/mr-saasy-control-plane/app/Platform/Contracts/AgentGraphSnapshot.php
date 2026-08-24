<?php

namespace App\Platform\Contracts;

final readonly class AgentGraphSnapshot
{
    /**
     * @param list<array<string, mixed>> $nodes
     * @param list<array<string, mixed>> $edges
     * @param list<array<string, mixed>> $tasks
     * @param list<array<string, mixed>> $activity
     */
    public function __construct(
        public array $nodes,
        public array $edges,
        public array $tasks,
        public array $activity,
    ) {}

    /** @return array{nodes: array, edges: array, tasks: array, activity: array} */
    public function toArray(): array
    {
        return [
            'nodes' => $this->nodes,
            'edges' => $this->edges,
            'tasks' => $this->tasks,
            'activity' => $this->activity,
        ];
    }
}
