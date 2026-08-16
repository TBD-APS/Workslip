<?php

namespace Tests\Unit\AI\Routing;

use App\AI\Application\Routing\AgentRole;
use App\AI\Application\Routing\RoutingConfiguration;
use PHPUnit\Framework\TestCase;

final class ConfiguredRolePolicyTest extends TestCase
{
    public function test_maintained_routing_config_declares_every_v01_role_with_valid_vocabulary(): void
    {
        /** @var array<string, mixed> $config */
        $config = require dirname(__DIR__, 4).'/config/agent-routing.php';

        $registry = RoutingConfiguration::fromArray($config);

        foreach (AgentRole::cases() as $role) {
            self::assertSame($role, $registry->binding($role)->role);
        }
    }
}
