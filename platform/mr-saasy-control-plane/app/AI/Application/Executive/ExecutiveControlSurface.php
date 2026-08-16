<?php

namespace App\AI\Application\Executive;

enum ExecutiveControlSurface: string
{
    case Permissions = 'permissions';
    case BudgetLimit = 'budget_limit';
    case GovernancePolicy = 'governance_policy';
}
