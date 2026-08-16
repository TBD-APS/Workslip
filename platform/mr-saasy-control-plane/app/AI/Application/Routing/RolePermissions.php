<?php

namespace App\AI\Application\Routing;

final readonly class RolePermissions
{
    public function __construct(
        public bool $canExecuteWrite,
        public bool $canReview,
        public bool $canApprove,
    ) {
    }
}
