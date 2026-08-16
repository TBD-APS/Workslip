<?php

namespace App\AI\Application\Executive;

final class ExecutiveSelfAuthorityPolicy
{
    public static function canModifyOwnControlSurface(ExecutiveControlSurface $surface): bool
    {
        return false;
    }
}
