<?php

namespace App\ProductAdapters\Contracts;

use App\Platform\Contracts\TenantContext;

interface ProductContextPort
{
    /**
     * @param list<string> $fields
     * @return array<string, scalar|array|null>
     */
    public function loadSanitizedContext(TenantContext $tenant, array $fields): array;
}
