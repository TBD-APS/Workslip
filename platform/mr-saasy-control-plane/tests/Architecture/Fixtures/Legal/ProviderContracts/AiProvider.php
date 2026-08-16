<?php

namespace Tests\Architecture\Fixtures\Legal\ProviderContracts;

interface AiProvider
{
    public function invoke(string $prompt): string;
}
