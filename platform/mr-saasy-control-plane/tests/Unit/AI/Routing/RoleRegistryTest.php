<?php

namespace Tests\Unit\AI\Routing;

use App\AI\Application\Routing\AgentRole;
use App\AI\Application\Routing\GovernedAction;
use App\AI\Application\Routing\HumanApprovalPolicy;
use App\AI\Application\Routing\RoutingConfiguration;
use App\AI\Application\Routing\RoutingUnavailable;
use App\AI\Application\Routing\RunProvenance;
use App\AI\Application\Routing\SeparationOfDutiesPolicy;
use DateTimeImmutable;
use InvalidArgumentException;
use PHPUnit\Framework\Attributes\DataProvider;
use PHPUnit\Framework\TestCase;

final class RoleRegistryTest extends TestCase
{
    public function test_routes_to_primary_when_all_capability_and_tool_requirements_are_satisfied(): void
    {
        $registry = RoutingConfiguration::fromArray($this->config(
            primaryCapabilities: ['reasoning', 'coding'],
            primaryTools: ['repository_read'],
        ));

        $decision = $registry->route(AgentRole::EngineeringOrchestrator);

        self::assertSame('synthetic-a', $decision->target->provider);
        self::assertSame('model-a', $decision->target->model);
        self::assertFalse($decision->usedFallback);
    }

    public function test_uses_fallback_when_primary_lacks_required_capability(): void
    {
        $registry = RoutingConfiguration::fromArray($this->config(
            primaryCapabilities: ['reasoning'],
            primaryTools: ['repository_read'],
            fallbackCapabilities: ['reasoning', 'coding'],
            fallbackTools: ['repository_read'],
        ));

        $decision = $registry->route(AgentRole::EngineeringOrchestrator);

        self::assertSame('synthetic-b', $decision->target->provider);
        self::assertSame('model-b', $decision->target->model);
        self::assertTrue($decision->usedFallback);
    }

    public function test_unconfigured_primary_can_fall_back_without_domain_change(): void
    {
        $config = $this->config(
            primaryCapabilities: ['reasoning', 'coding'],
            primaryTools: ['repository_read'],
            fallbackCapabilities: ['reasoning', 'coding'],
            fallbackTools: ['repository_read'],
        );
        $config['models']['primary']['model'] = null;

        $registry = RoutingConfiguration::fromArray($config);
        $decision = $registry->route(AgentRole::EngineeringOrchestrator);

        self::assertSame('model-b', $decision->target->model);
        self::assertTrue($decision->usedFallback);
    }

    public function test_missing_required_tool_fails_closed_when_no_fallback_is_eligible(): void
    {
        $config = $this->config(
            primaryCapabilities: ['reasoning', 'coding'],
            primaryTools: [],
            fallbackCapabilities: ['reasoning', 'coding'],
            fallbackTools: [],
        );

        $registry = RoutingConfiguration::fromArray($config);

        $this->expectException(RoutingUnavailable::class);
        $registry->route(AgentRole::EngineeringOrchestrator);
    }

    public function test_unknown_capability_is_rejected_during_configuration_loading(): void
    {
        $config = $this->config(
            primaryCapabilities: ['reasoning', 'not-a-real-capability'],
            primaryTools: ['repository_read'],
        );

        $this->expectException(InvalidArgumentException::class);
        RoutingConfiguration::fromArray($config);
    }

    public function test_role_cannot_reference_unknown_target_alias(): void
    {
        $config = $this->config(
            primaryCapabilities: ['reasoning', 'coding'],
            primaryTools: ['repository_read'],
        );
        $config['roles']['engineering_orchestrator']['primary'] = 'typo-target';

        $this->expectException(InvalidArgumentException::class);
        RoutingConfiguration::fromArray($config);
    }

    public function test_same_agent_cannot_be_sole_approving_reviewer(): void
    {
        $implementation = $this->provenance('implementation', 'agent-1', 'provider-a', 'model-a');
        $review = $this->provenance('review', 'agent-1', 'provider-b', 'model-b', AgentRole::IndependentPrReviewer);

        self::assertFalse(SeparationOfDutiesPolicy::canBeSoleApprovingReview($implementation, $review));
    }

    public function test_same_provider_and_model_cannot_be_sole_approving_reviewer_even_with_different_agent_id(): void
    {
        $implementation = $this->provenance('implementation', 'agent-1', 'provider-a', 'model-a');
        $review = $this->provenance('review', 'agent-2', 'provider-a', 'model-a', AgentRole::IndependentPrReviewer);

        self::assertFalse(SeparationOfDutiesPolicy::canBeSoleApprovingReview($implementation, $review));
    }

    public function test_different_agent_and_model_can_supply_independent_review_signal(): void
    {
        $implementation = $this->provenance('implementation', 'agent-1', 'provider-a', 'model-a');
        $review = $this->provenance('review', 'agent-2', 'provider-b', 'model-b', AgentRole::IndependentPrReviewer);

        self::assertTrue(SeparationOfDutiesPolicy::canBeSoleApprovingReview($implementation, $review));
    }

    #[DataProvider('humanGatedActions')]
    public function test_irreversible_or_public_actions_are_human_gated(GovernedAction $action): void
    {
        self::assertTrue(HumanApprovalPolicy::requiresHumanApproval($action));
    }

    /** @return iterable<string, array{GovernedAction}> */
    public static function humanGatedActions(): iterable
    {
        foreach (GovernedAction::cases() as $action) {
            yield $action->value => [$action];
        }
    }

    /**
     * @param list<string> $primaryCapabilities
     * @param list<string> $primaryTools
     * @param list<string> $fallbackCapabilities
     * @param list<string> $fallbackTools
     * @return array<string, mixed>
     */
    private function config(
        array $primaryCapabilities,
        array $primaryTools,
        array $fallbackCapabilities = ['reasoning', 'coding'],
        array $fallbackTools = ['repository_read'],
    ): array {
        return [
            'models' => [
                'primary' => [
                    'provider' => 'synthetic-a',
                    'model' => 'model-a',
                    'capabilities' => $primaryCapabilities,
                    'tools' => $primaryTools,
                ],
                'fallback' => [
                    'provider' => 'synthetic-b',
                    'model' => 'model-b',
                    'capabilities' => $fallbackCapabilities,
                    'tools' => $fallbackTools,
                ],
            ],
            'roles' => [
                'engineering_orchestrator' => [
                    'primary' => 'primary',
                    'fallback' => 'fallback',
                    'required_capabilities' => ['reasoning', 'coding'],
                    'required_tools' => ['repository_read'],
                    'permissions' => [
                        'execute_write' => false,
                        'review' => true,
                        'approve' => false,
                    ],
                    'preference' => 'quality',
                ],
            ],
        ];
    }

    private function provenance(
        string $runId,
        string $agentId,
        string $provider,
        string $model,
        AgentRole $role = AgentRole::ImplementationComplex,
    ): RunProvenance {
        return new RunProvenance(
            $runId,
            $role,
            $agentId,
            $provider,
            $model,
            new DateTimeImmutable('2026-08-16T05:00:00+00:00'),
            new DateTimeImmutable('2026-08-16T05:01:00+00:00'),
            ['evidence://synthetic'],
        );
    }
}
