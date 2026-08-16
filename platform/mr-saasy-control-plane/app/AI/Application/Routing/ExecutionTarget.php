<?php

namespace App\AI\Application\Routing;

use InvalidArgumentException;

final readonly class ExecutionTarget
{
    /**
     * @param list<Capability> $capabilities
     * @param list<ToolCapability> $tools
     */
    public function __construct(
        public string $key,
        public string $provider,
        public string $model,
        public array $capabilities,
        public array $tools = [],
        public bool $enabled = true,
    ) {
        foreach ([$key, $provider, $model] as $value) {
            if (trim($value) === '') {
                throw new InvalidArgumentException('Execution target key, provider and model are required.');
            }
        }
    }

    /** @param list<Capability> $required */
    public function supportsCapabilities(array $required): bool
    {
        return $this->containsAll($this->capabilities, $required);
    }

    /** @param list<ToolCapability> $required */
    public function supportsTools(array $required): bool
    {
        return $this->containsAll($this->tools, $required);
    }

    /** @param list<object> $available @param list<object> $required */
    private function containsAll(array $available, array $required): bool
    {
        $availableValues = array_map(static fn (object $item): string => $item->value, $available);

        foreach ($required as $item) {
            if (!in_array($item->value, $availableValues, true)) {
                return false;
            }
        }

        return true;
    }
}
