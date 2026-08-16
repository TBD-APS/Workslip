<?php

namespace App\AI\Application\Executive;

enum ExecutiveDecisionDisposition: string
{
    case Recommendation = 'recommendation';
    case RequiresFounderApproval = 'requires_founder_approval';
}

final class ExecutiveDecisionAuthority
{
    public static function disposition(ExecutiveDecisionClass $decision): ExecutiveDecisionDisposition
    {
        return match ($decision) {
            ExecutiveDecisionClass::BacklogPrioritization,
            ExecutiveDecisionClass::ExperimentDesign,
            ExecutiveDecisionClass::ResourceAllocationProposal,
            ExecutiveDecisionClass::ContentCampaignHypothesis,
            ExecutiveDecisionClass::TechnicalSequencing,
            ExecutiveDecisionClass::ReversibleOperationalChange => ExecutiveDecisionDisposition::Recommendation,

            ExecutiveDecisionClass::PricingChange,
            ExecutiveDecisionClass::ContractTerms,
            ExecutiveDecisionClass::LegalCommitment,
            ExecutiveDecisionClass::MaterialSpend,
            ExecutiveDecisionClass::EmploymentCommitment,
            ExecutiveDecisionClass::PublicMaterialStatement,
            ExecutiveDecisionClass::EquityOwnership,
            ExecutiveDecisionClass::ProductionDestructiveAction,
            ExecutiveDecisionClass::IrreversibleCommercialCommitment,
            ExecutiveDecisionClass::GovernancePolicyChange => ExecutiveDecisionDisposition::RequiresFounderApproval,
        };
    }

    public static function requiresFounderApproval(ExecutiveDecisionClass $decision): bool
    {
        return self::disposition($decision) === ExecutiveDecisionDisposition::RequiresFounderApproval;
    }
}
