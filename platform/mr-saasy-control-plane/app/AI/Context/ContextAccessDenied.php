<?php

namespace App\AI\Context;

use RuntimeException;

/**
 * Raised when the Context/Policy Gateway refuses a request.
 *
 * The message intentionally carries only the reason and non-sensitive request
 * metadata (capability name, field names) — never customer payload.
 */
final class ContextAccessDenied extends RuntimeException
{
    private function __construct(
        public readonly ContextDenialReason $reason,
        string $message,
    ) {
        parent::__construct($message);
    }

    public static function because(ContextDenialReason $reason, string $detail = ''): self
    {
        $message = "Context access denied ({$reason->value})";
        if (trim($detail) !== '') {
            $message .= ": {$detail}";
        }

        return new self($reason, $message);
    }
}
