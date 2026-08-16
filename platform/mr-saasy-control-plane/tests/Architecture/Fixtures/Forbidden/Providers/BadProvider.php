<?php

namespace Tests\Architecture\Fixtures\Forbidden\Providers;

use Tests\Architecture\Fixtures\Forbidden\Persistence\PlatformRecord;

final readonly class BadProvider
{
    public function __construct(private PlatformRecord $record)
    {
    }
}
