<?php

namespace Tests\Architecture\Fixtures\ForbiddenSymbols;

final class BadCredentialProvider
{
    public function password(): mixed
    {
        return env('DB_PASSWORD');
    }
}
