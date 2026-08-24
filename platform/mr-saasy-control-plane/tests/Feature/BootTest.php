<?php

namespace Tests\Feature;

use Tests\TestCase;

final class BootTest extends TestCase
{
    public function test_health_endpoint_boots_without_product_credentials(): void
    {
        $this->get('/up')->assertOk();
    }

    public function test_root_exposes_gate_zero_security_state_and_graph_entrypoint(): void
    {
        $this->get('/')
            ->assertOk()
            ->assertExactJson([
                'service' => 'mr-saasy-control-plane',
                'state' => 'gate-0',
                'directDbAccess' => false,
                'agentGraph' => '/agent-graph',
            ]);
    }
}
