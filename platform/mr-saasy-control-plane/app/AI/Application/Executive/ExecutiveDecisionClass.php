<?php

namespace App\AI\Application\Executive;

enum ExecutiveDecisionClass: string
{
    case BacklogPrioritization = 'backlog_prioritization';
    case ExperimentDesign = 'experiment_design';
    case ResourceAllocationProposal = 'resource_allocation_proposal';
    case ContentCampaignHypothesis = 'content_campaign_hypothesis';
    case TechnicalSequencing = 'technical_sequencing';
    case ReversibleOperationalChange = 'reversible_operational_change';

    case PricingChange = 'pricing_change';
    case ContractTerms = 'contract_terms';
    case LegalCommitment = 'legal_commitment';
    case MaterialSpend = 'material_spend';
    case EmploymentCommitment = 'employment_commitment';
    case PublicMaterialStatement = 'public_material_statement';
    case EquityOwnership = 'equity_ownership';
    case ProductionDestructiveAction = 'production_destructive_action';
    case IrreversibleCommercialCommitment = 'irreversible_commercial_commitment';
    case GovernancePolicyChange = 'governance_policy_change';
}
