<?php

namespace App\AI\Application\Routing;

use BackedEnum;
use InvalidArgumentException;

final class RoutingConfiguration
{
    /** @param array<string, mixed> $config */
    public static function fromArray(array $config): RoleRegistry
    {
        $modelConfigs = self::arrayValue($config, 'models');
        $roleConfigs = self::arrayValue($config, 'roles');

        $targets = [];
        foreach ($modelConfigs as $key => $targetConfig) {
            if (!is_string($key) || !is_array($targetConfig)) {
                throw new InvalidArgumentException('Every model target must have a string alias and array configuration.');
            }

            $provider = self::nullableString($targetConfig['provider'] ?? null);
            $model = self::nullableString($targetConfig['model'] ?? null);
            $capabilities = self::enumList($targetConfig['capabilities'] ?? [], Capability::class, "models.{$key}.capabilities");
            $tools = self::enumList($targetConfig['tools'] ?? [], ToolCapability::class, "models.{$key}.tools");
            $enabled = (bool) ($targetConfig['enabled'] ?? true);

            // A declared alias with no concrete provider/model is intentionally unavailable,
            // allowing provider/model rollout to be environment-driven without weakening role policy.
            if ($provider === null || $model === null) {
                continue;
            }

            $targets[] = new ExecutionTarget($key, $provider, $model, $capabilities, $tools, $enabled);
        }

        $knownTargetKeys = array_fill_keys(array_keys($modelConfigs), true);
        $bindings = [];

        foreach ($roleConfigs as $roleName => $roleConfig) {
            if (!is_string($roleName) || !is_array($roleConfig)) {
                throw new InvalidArgumentException('Every role must have a string name and array configuration.');
            }

            $role = AgentRole::tryFrom($roleName)
                ?? throw new InvalidArgumentException("Unknown agent role '{$roleName}'.");

            $primary = self::requiredString($roleConfig, 'primary', "roles.{$roleName}.primary");
            $fallback = self::nullableString($roleConfig['fallback'] ?? null);

            foreach (array_filter([$primary, $fallback]) as $targetKey) {
                if (!isset($knownTargetKeys[$targetKey])) {
                    throw new InvalidArgumentException(
                        "Role '{$roleName}' references unknown execution target '{$targetKey}'.",
                    );
                }
            }

            $permissions = self::arrayValue($roleConfig, 'permissions');

            $bindings[] = new RoleBinding(
                $role,
                $primary,
                $fallback,
                self::enumList(
                    $roleConfig['required_capabilities'] ?? [],
                    Capability::class,
                    "roles.{$roleName}.required_capabilities",
                ),
                self::enumList(
                    $roleConfig['required_tools'] ?? [],
                    ToolCapability::class,
                    "roles.{$roleName}.required_tools",
                ),
                new RolePermissions(
                    (bool) ($permissions['execute_write'] ?? false),
                    (bool) ($permissions['review'] ?? false),
                    (bool) ($permissions['approve'] ?? false),
                ),
                RoutingPreference::tryFrom((string) ($roleConfig['preference'] ?? RoutingPreference::Balanced->value))
                    ?? throw new InvalidArgumentException("Invalid routing preference for role '{$roleName}'."),
            );
        }

        return new RoleRegistry($targets, $bindings);
    }

    /** @return array<mixed> */
    private static function arrayValue(array $input, string $key): array
    {
        $value = $input[$key] ?? [];
        if (!is_array($value)) {
            throw new InvalidArgumentException("'{$key}' must be an array.");
        }

        return $value;
    }

    private static function requiredString(array $input, string $key, string $path): string
    {
        $value = self::nullableString($input[$key] ?? null);
        if ($value === null) {
            throw new InvalidArgumentException("'{$path}' is required.");
        }

        return $value;
    }

    private static function nullableString(mixed $value): ?string
    {
        if (!is_string($value)) {
            return null;
        }

        $value = trim($value);
        return $value === '' ? null : $value;
    }

    /**
     * @template T of BackedEnum
     * @param mixed $values
     * @param class-string<T> $enum
     * @return list<T>
     */
    private static function enumList(mixed $values, string $enum, string $path): array
    {
        if (!is_array($values)) {
            throw new InvalidArgumentException("'{$path}' must be an array.");
        }

        $result = [];
        foreach ($values as $value) {
            if (!is_string($value)) {
                throw new InvalidArgumentException("'{$path}' contains a non-string value.");
            }

            $parsed = $enum::tryFrom($value);
            if ($parsed === null) {
                throw new InvalidArgumentException("'{$path}' contains unknown value '{$value}'.");
            }

            $result[] = $parsed;
        }

        return $result;
    }
}
