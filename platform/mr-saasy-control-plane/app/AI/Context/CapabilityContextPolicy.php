<?php

namespace App\AI\Context;

use InvalidArgumentException;

/**
 * Declares, per capability, which product-context fields are exposed and which of
 * those must be masked before an agent sees them.
 *
 * This is provider-neutral context-shaping policy: minimization (the permitted
 * field allow-list) and masking hooks. Tenant/capability authorization is a
 * separate concern owned by the platform {@see \App\Platform\Policy\ContextPolicy}.
 */
final class CapabilityContextPolicy
{
    /** @var array<string, list<string>> */
    private array $permitted = [];

    /** @var array<string, list<string>> */
    private array $masked = [];

    /**
     * @param array<string, array{fields?: list<string>, masked?: list<string>}> $capabilities
     */
    public function __construct(array $capabilities)
    {
        foreach ($capabilities as $capability => $definition) {
            if (!is_string($capability) || trim($capability) === '') {
                throw new InvalidArgumentException('Every capability must have a non-empty string name.');
            }

            if (!is_array($definition)) {
                throw new InvalidArgumentException("Capability '{$capability}' must map to a definition array.");
            }

            $fields = self::stringList($definition['fields'] ?? [], "capabilities.{$capability}.fields");
            $masked = self::stringList($definition['masked'] ?? [], "capabilities.{$capability}.masked");

            foreach ($masked as $maskedField) {
                if (!in_array($maskedField, $fields, true)) {
                    throw new InvalidArgumentException(
                        "Capability '{$capability}' masks '{$maskedField}', which is not one of its permitted fields.",
                    );
                }
            }

            $this->permitted[$capability] = $fields;
            $this->masked[$capability] = $masked;
        }
    }

    public function knows(string $capability): bool
    {
        return array_key_exists($capability, $this->permitted);
    }

    /** @return list<string> */
    public function permittedFields(string $capability): array
    {
        return $this->permitted[$capability] ?? [];
    }

    /** @return list<string> */
    public function maskedFields(string $capability): array
    {
        return $this->masked[$capability] ?? [];
    }

    /**
     * @return list<string>
     */
    private static function stringList(mixed $value, string $path): array
    {
        if (!is_array($value)) {
            throw new InvalidArgumentException("'{$path}' must be an array of field names.");
        }

        $result = [];
        foreach ($value as $field) {
            if (!is_string($field) || trim($field) === '') {
                throw new InvalidArgumentException("'{$path}' must contain only non-empty field names.");
            }

            $field = trim($field);
            if (!in_array($field, $result, true)) {
                $result[] = $field;
            }
        }

        return $result;
    }
}
