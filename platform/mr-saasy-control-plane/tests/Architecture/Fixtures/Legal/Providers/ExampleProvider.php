<?php

namespace Tests\Architecture\Fixtures\Legal\Providers;

use Tests\Architecture\Fixtures\Legal\Http\HttpTransport;
use Tests\Architecture\Fixtures\Legal\ProviderContracts\AiProvider;

final readonly class ExampleProvider implements AiProvider
{
    public function __construct(private HttpTransport $http)
    {
    }

    public function invoke(string $prompt): string
    {
        return $this->http->post('https://provider.invalid');
    }
}
