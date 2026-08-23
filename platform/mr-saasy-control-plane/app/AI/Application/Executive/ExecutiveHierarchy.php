<?php

namespace App\AI\Application\Executive;

use App\AI\Application\Routing\AgentRole;

final class ExecutiveHierarchy
{
    /** @return list<AgentRole> */
    public static function executiveRoles(): array
    {
        return [
            AgentRole::ChiefExecutive,
            AgentRole::ChiefOperatingOfficer,
            AgentRole::ChiefTechnologyOfficer,
            AgentRole::ChiefProductOfficer,
            AgentRole::ChiefMarketingGrowth,
            AgentRole::ChiefFinanceCommercial,
        ];
    }

    public static function isExecutive(AgentRole $role): bool
    {
        return in_array($role, self::executiveRoles(), true);
    }

    public static function canDelegate(AgentRole $from, AgentRole $to): bool
    {
        return in_array($to, self::directReports($from), true);
    }

    /** @return list<AgentRole> */
    public static function directReports(AgentRole $role): array
    {
        return match ($role) {
            AgentRole::ChiefExecutive => [
                AgentRole::ChiefOperatingOfficer,
                AgentRole::ChiefTechnologyOfficer,
                AgentRole::ChiefProductOfficer,
                AgentRole::ChiefMarketingGrowth,
                AgentRole::ChiefFinanceCommercial,
            ],
            AgentRole::ChiefOperatingOfficer => [
                AgentRole::TriageSummary,
                AgentRole::QaVerification,
                AgentRole::RepoInvestigator,
            ],
            AgentRole::ChiefTechnologyOfficer => [
                AgentRole::EngineeringOrchestrator,
                AgentRole::SoftwareArchitect,
                AgentRole::ImplementationComplex,
                AgentRole::ImplementationStandard,
                AgentRole::RepoInvestigator,
                AgentRole::IndependentPrReviewer,
                AgentRole::SecurityReviewer,
                AgentRole::QaVerification,
                AgentRole::DocumentationSteward,
            ],
            AgentRole::ChiefProductOfficer => [
                AgentRole::ProductMarketing,
                AgentRole::MarketIntelligence,
                AgentRole::RepoInvestigator,
            ],
            AgentRole::ChiefMarketingGrowth => [
                AgentRole::ContentStrategist,
                AgentRole::ContentResearcher,
                AgentRole::ContentBuilder,
                AgentRole::MarketIntelligence,
                AgentRole::ProductMarketing,
            ],
            AgentRole::ChiefFinanceCommercial => [
                AgentRole::CommercialStrategist,
                AgentRole::MarketIntelligence,
            ],
            default => [],
        };
    }
}
