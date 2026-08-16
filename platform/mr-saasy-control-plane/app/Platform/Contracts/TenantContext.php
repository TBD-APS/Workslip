<?php

namespace App\Platform\Contracts;

use InvalidArgumentException;

final readonly class TenantContext
{
    public function __construct(public string $tenantId)
    {
        if (trim($tenantId) === '') {
            throw new InvalidArgumentException('Tenant id is required.');
        }
    }
}
