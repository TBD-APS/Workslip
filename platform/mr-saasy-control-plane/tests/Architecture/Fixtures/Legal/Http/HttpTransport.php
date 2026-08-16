<?php

namespace Tests\Architecture\Fixtures\Legal\Http;

interface HttpTransport
{
    public function post(string $url): string;
}
