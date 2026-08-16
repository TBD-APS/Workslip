<?php

namespace App\AI\Application\Routing;

enum RoutingPreference: string
{
    case Quality = 'quality';
    case Balanced = 'balanced';
    case Cost = 'cost';
    case Latency = 'latency';
}
