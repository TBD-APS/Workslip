<?php

namespace App\AI\Context;

use App\Platform\Contracts\TenantContext;

interface ContextGateway
{
    /** @param list<string> $fields */
    public function load(TenantContext $tenant, string $capability, array $fields): SanitizedContext;
}
