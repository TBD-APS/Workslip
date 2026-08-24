<?php

namespace Tests\Feature;

use Tests\TestCase;

final class AgentGraphTest extends TestCase
{
    public function test_graph_snapshot_is_available_without_enabling_execution(): void
    {
        $this->getJson('/api/agent-graph')
            ->assertOk()
            ->assertJsonPath('mode', 'wiring-prototype')
            ->assertJsonPath('executionEnabled', false)
            ->assertJsonFragment(['id' => 'pi', 'label' => 'Pi'])
            ->assertJsonFragment(['id' => 'ci', 'label' => 'CI Gate']);
    }

    public function test_delegation_preview_projects_handoff_but_does_not_enable_execution(): void
    {
        $this->getJson('/api/agent-graph/preview-delegation?taskId=SASSY-GRAPH-UI&from=pi&to=kimi')
            ->assertOk()
            ->assertJsonPath('mode', 'preview')
            ->assertJsonPath('executionEnabled', false)
            ->assertJsonFragment(['id' => 'delegation:SASSY-GRAPH-UI', 'from' => 'pi', 'to' => 'kimi']);
    }

    public function test_delegation_to_non_agent_node_fails_closed(): void
    {
        $this->getJson('/api/agent-graph/preview-delegation?taskId=SASSY-GRAPH-UI&from=pi&to=github')
            ->assertStatus(422)
            ->assertJsonFragment(['message' => 'Tasks can only be delegated to an agent-capable node.']);
    }
}
