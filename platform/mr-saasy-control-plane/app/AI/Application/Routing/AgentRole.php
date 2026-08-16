<?php

namespace App\AI\Application\Routing;

enum AgentRole: string
{
    case EngineeringOrchestrator = 'engineering_orchestrator';
    case SoftwareArchitect = 'software_architect';
    case ImplementationComplex = 'implementation_complex';
    case ImplementationStandard = 'implementation_standard';
    case RepoInvestigator = 'repo_investigator';
    case IndependentPrReviewer = 'independent_pr_reviewer';
    case SecurityReviewer = 'security_reviewer';
    case QaVerification = 'qa_verification';
    case TriageSummary = 'triage_summary';

    case ContentStrategist = 'content_strategist';
    case ContentResearcher = 'content_researcher';
    case ContentBuilder = 'content_builder';
    case MarketIntelligence = 'market_intelligence';
    case CommercialStrategist = 'commercial_strategist';
    case ProductMarketing = 'product_marketing';
}
