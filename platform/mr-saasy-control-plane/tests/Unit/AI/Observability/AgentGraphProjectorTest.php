<?php

namespace Tests\Unit\AI\Observability;

use App\AI\Application\Observability\AgentGraphProjector;
use PHPUnit\Framework\TestCase;

final class AgentGraphProjectorTest extends TestCase
{
    public function test_delegation_projects_owner_and_edge(): void
    {
        $snapshot = (new AgentGraphProjector())->project([
            ['type' => 'NodeObserved', 'payload' => ['id' => 'pi', 'label' => 'Pi', 'kind' => 'orchestrator']],
            ['type' => 'NodeObserved', 'payload' => ['id' => 'kimi', 'label' => 'Kimi', 'kind' => 'agent']],
            ['type' => 'TaskObserved', 'payload' => ['id' => 'TASK-1', 'title' => 'Build graph', 'owner' => 'pi', 'status' => 'planned']],
            ['type' => 'TaskDelegated', 'payload' => ['taskId' => 'TASK-1', 'from' => 'pi', 'to' => 'kimi']],
        ]);

        self::assertSame('kimi', $snapshot->tasks[0]['owner']);
        self::assertSame('running', $snapshot->tasks[0]['status']);
        self::assertSame('pi', $snapshot->edges[0]['from']);
        self::assertSame('kimi', $snapshot->edges[0]['to']);
        self::assertSame('delegated_to', $snapshot->edges[0]['kind']);
    }
}
