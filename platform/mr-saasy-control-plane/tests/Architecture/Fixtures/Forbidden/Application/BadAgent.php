<?php

namespace Tests\Architecture\Fixtures\Forbidden\Application;

use Tests\Architecture\Fixtures\Forbidden\Persistence\PlatformRecord;

final readonly class BadAgent
{
    public function __construct(private PlatformRecord $record)
    {
    }
}
