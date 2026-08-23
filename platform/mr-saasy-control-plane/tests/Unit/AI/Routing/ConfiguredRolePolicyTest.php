<?php

namespace Tests\Unit\AI\Routing;

use App\AI\Application\Routing\AgentRole;
use App\AI\Application\Routing\RoutingConfiguration;
use App\AI\Application\Routing\ToolCapability;
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

    public function test_documentation_steward_has_only_the_bounded_documentation_write_capability(): void
    {
        /** @var array<string, mixed> $config */
        $config = require dirname(__DIR__, 4).'/config/agent-routing.php';

        $binding = RoutingConfiguration::fromArray($config)->binding(AgentRole::DocumentationSteward);

        self::assertTrue($binding->permissions->canExecuteWrite);
        self::assertFalse($binding->permissions->canApprove);
        self::assertContains(ToolCapability::DocumentationWrite, $binding->requiredTools);
        self::assertContains(ToolCapability::RepositoryRead, $binding->requiredTools);
        self::assertContains(ToolCapability::PullRequestRead, $binding->requiredTools);
    }
}
