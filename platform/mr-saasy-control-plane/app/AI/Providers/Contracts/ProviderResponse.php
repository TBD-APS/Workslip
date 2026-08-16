<?php

namespace App\AI\Providers\Contracts;

final readonly class ProviderResponse
{
    /** @param array<string, scalar|null> $usage */
    public function __construct(
        public string $content,
        public array $usage = [],
    ) {
    }
}
