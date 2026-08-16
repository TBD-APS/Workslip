<?php

namespace Tests\Unit\AI\Executive;

use App\AI\Application\Executive\ExecutiveDecisionAuthority;
use App\AI\Application\Executive\ExecutiveDecisionClass;
use App\AI\Application\Executive\ExecutiveDecisionDisposition;
use App\AI\Application\Executive\ExecutiveHierarchy;
use App\AI\Application\Executive\ExecutiveRecommendation;
use App\AI\Application\Routing\AgentRole;
use App\AI\Application\Routing\RunProvenance;
use DateTimeImmutable;
use InvalidArgumentException;
use PHPUnit\Framework\Attributes\DataProvider;
use PHPUnit\Framework\TestCase;

final class ExecutiveLeadershipTest extends TestCase
{
    public function test_ceo_delegates_to_functional_executives_but_not_directly_to_implementation_worker(): void
    {
        self::assertTrue(ExecutiveHierarchy::canDelegate(
            AgentRole::ChiefExecutive,
            AgentRole::ChiefTechnologyOfficer,
        ));
        self::assertTrue(ExecutiveHierarchy::canDelegate(
            AgentRole::ChiefExecutive,
            AgentRole::ChiefMarketingGrowth,
        ));
        self::assertFalse(ExecutiveHierarchy::canDelegate(
            AgentRole::ChiefExecutive,
            AgentRole::ImplementationStandard,
        ));
    }

    public function test_cto_and_cmo_delegate_only_into_their_department_roles(): void
    {
        self::assertTrue(ExecutiveHierarchy::canDelegate(
            AgentRole::ChiefTechnologyOfficer,
            AgentRole::EngineeringOrchestrator,
        ));
        self::assertFalse(ExecutiveHierarchy::canDelegate(
            AgentRole::ChiefTechnologyOfficer,
            AgentRole::ContentBuilder,
        ));

        self::assertTrue(ExecutiveHierarchy::canDelegate(
            AgentRole::ChiefMarketingGrowth,
            AgentRole::ContentBuilder,
        ));
        self::assertFalse(ExecutiveHierarchy::canDelegate(
            AgentRole::ChiefMarketingGrowth,
            AgentRole::ImplementationComplex,
        ));
    }

    #[DataProvider('recommendationDecisions')]
    public function test_reversible_executive_work_remains_a_recommendation(ExecutiveDecisionClass $decision): void
    {
        self::assertSame(
            ExecutiveDecisionDisposition::Recommendation,
            ExecutiveDecisionAuthority::disposition($decision),
        );
    }

    #[DataProvider('founderGatedDecisions')]
    public function test_high_impact_decisions_require_founder_approval(ExecutiveDecisionClass $decision): void
    {
        self::assertTrue(ExecutiveDecisionAuthority::requiresFounderApproval($decision));
    }

    public function test_material_executive_recommendation_retains_run_and_evidence_provenance(): void
    {
        $startedAt = new DateTimeImmutable('2026-08-16T05:00:00+00:00');
        $run = new RunProvenance(
            'run-ceo-1',
            AgentRole::ChiefExecutive,
            'ceo-agent',
            'synthetic-provider',
            'synthetic-model',
            $startedAt,
            $startedAt->modify('+1 minute'),
            ['linear://WOR-588', 'control-center://portfolio/current'],
        );

        $recommendation = new ExecutiveRecommendation(
            'decision-1',
            AgentRole::ChiefExecutive,
            ExecutiveDecisionClass::PricingChange,
            'Test a new launch price after design-partner evidence is collected.',
            $run,
            $startedAt->modify('+1 minute'),
            ['linear://WOR-432'],
            ['evidence://pricing-interviews'],
        );

        self::assertSame(ExecutiveDecisionDisposition::RequiresFounderApproval, $recommendation->disposition());
        self::assertSame('synthetic-provider', $recommendation->run->provider);
        self::assertContains('evidence://pricing-interviews', $recommendation->evidenceReferences);
    }

    public function test_non_executive_cannot_own_executive_recommendation(): void
    {
        $startedAt = new DateTimeImmutable('2026-08-16T05:00:00+00:00');
        $run = new RunProvenance(
            'run-worker-1',
            AgentRole::ImplementationStandard,
            'worker-agent',
            'synthetic-provider',
            'synthetic-model',
            $startedAt,
        );

        $this->expectException(InvalidArgumentException::class);

        new ExecutiveRecommendation(
            'decision-invalid',
            AgentRole::ImplementationStandard,
            ExecutiveDecisionClass::TechnicalSequencing,
            'Invalid worker-owned executive decision.',
            $run,
            $startedAt,
        );
    }

    /** @return iterable<string, array{ExecutiveDecisionClass}> */
    public static function recommendationDecisions(): iterable
    {
        foreach ([
            ExecutiveDecisionClass::BacklogPrioritization,
            ExecutiveDecisionClass::ExperimentDesign,
            ExecutiveDecisionClass::ResourceAllocationProposal,
            ExecutiveDecisionClass::ContentCampaignHypothesis,
            ExecutiveDecisionClass::TechnicalSequencing,
            ExecutiveDecisionClass::ReversibleOperationalChange,
        ] as $decision) {
            yield $decision->value => [$decision];
        }
    }

    /** @return iterable<string, array{ExecutiveDecisionClass}> */
    public static function founderGatedDecisions(): iterable
    {
        foreach ([
            ExecutiveDecisionClass::PricingChange,
            ExecutiveDecisionClass::ContractTerms,
            ExecutiveDecisionClass::LegalCommitment,
            ExecutiveDecisionClass::MaterialSpend,
            ExecutiveDecisionClass::EmploymentCommitment,
            ExecutiveDecisionClass::PublicMaterialStatement,
            ExecutiveDecisionClass::EquityOwnership,
            ExecutiveDecisionClass::ProductionDestructiveAction,
            ExecutiveDecisionClass::IrreversibleCommercialCommitment,
            ExecutiveDecisionClass::GovernancePolicyChange,
        ] as $decision) {
            yield $decision->value => [$decision];
        }
    }
}
