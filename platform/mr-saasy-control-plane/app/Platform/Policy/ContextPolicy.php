<?php

namespace App\Platform\Policy;

use App\Platform\Contracts\TenantContext;

interface ContextPolicy
{
    public function allows(TenantContext $tenant, string $capability): bool;
}
