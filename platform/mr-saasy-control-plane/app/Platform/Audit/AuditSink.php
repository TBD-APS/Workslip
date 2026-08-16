<?php

namespace App\Platform\Audit;

interface AuditSink
{
    /** @param array<string, scalar|null> $metadata */
    public function record(string $event, array $metadata): void;
}
