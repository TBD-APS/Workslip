<?php

namespace App\AI\Application\Executive;

enum ExecutiveDecisionDisposition: string
{
    case Recommendation = 'recommendation';
    case RequiresFounderApproval = 'requires_founder_approval';
}
