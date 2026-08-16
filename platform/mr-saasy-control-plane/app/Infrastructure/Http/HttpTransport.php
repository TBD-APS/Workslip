<?php

namespace App\Infrastructure\Http;

interface HttpTransport
{
    /**
     * @param array<string, string> $headers
     * @param array<string, mixed> $payload
     * @return array<string, mixed>
     */
    public function postJson(string $url, array $headers, array $payload): array;
}
