<?php

namespace App\AI\Providers\Contracts;

final readonly class ProviderRequest
{
    /**
     * @param list<array{role: string, content: string}> $messages
     * @param array<string, scalar|null> $metadata
     */
    public function __construct(
        public array $messages,
        public array $metadata = [],
    ) {
    }
}
