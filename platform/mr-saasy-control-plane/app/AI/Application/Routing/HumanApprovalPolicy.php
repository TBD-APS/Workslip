<?php

namespace App\AI\Application\Routing;

enum GovernedAction: string
{
    case PublicContentPublish = 'public_content_publish';
    case PricingChange = 'pricing_change';
    case ContractChange = 'contract_change';
    case LegalCommitment = 'legal_commitment';
    case IrreversibleCommercialCommitment = 'irreversible_commercial_commitment';
    case GovernanceChange = 'governance_change';
}

final class HumanApprovalPolicy
{
    public static function requiresHumanApproval(GovernedAction $action): bool
    {
        return true;
    }
}
