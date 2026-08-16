<?php

namespace App\AI\Context;

final readonly class SanitizedContext
{
    /** @param array<string, scalar|array|null> $values */
    public function __construct(public array $values)
    {
    }
}
