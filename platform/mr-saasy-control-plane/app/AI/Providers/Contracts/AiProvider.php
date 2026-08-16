<?php

namespace App\AI\Providers\Contracts;

interface AiProvider
{
    public function invoke(ProviderRequest $request): ProviderResponse;
}
