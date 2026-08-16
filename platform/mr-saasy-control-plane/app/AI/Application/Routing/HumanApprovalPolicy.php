<?php

namespace App\AI\Application\Routing;

final class HumanApprovalPolicy
{
    public static function requiresHumanApproval(GovernedAction $action): bool
    {
        return true;
    }
}
