<?php

namespace Tests\Architecture\Fixtures\Uncovered\Application;

use Tests\Architecture\Fixtures\Uncovered\Shared\DbBridge;

final readonly class UnknownBridgeAgent
{
    public function __construct(private DbBridge $bridge)
    {
    }
}
