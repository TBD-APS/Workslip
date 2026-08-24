<?php

declare(strict_types=1);

use Symfony\Component\Yaml\Yaml;

require dirname(__DIR__) . '/vendor/autoload.php';

$repoRoot = dirname(__DIR__, 3);
$base = $repoRoot . '/Docs/operating-model';

$agents = Yaml::parseFile($base . '/agents/AGENT_REGISTRY.yml');
$matrix = Yaml::parseFile($base . '/agents/CAPABILITY_MATRIX.yml');
$tools = Yaml::parseFile($base . '/tools/TOOL_REGISTRY.yml');
$intents = Yaml::parseFile($base . '/execution/INTENT_MAP.yml');

$errors = [];

if (($matrix['default'] ?? null) !== 'deny') {
    $errors[] = 'CAPABILITY_MATRIX.yml must default to deny.';
}

$agentRegistry = $agents['agents'] ?? [];
$toolRegistry = $tools['tools'] ?? [];
$matrixAgents = $matrix['agents'] ?? [];

foreach ($matrixAgents as $agentName => $permissions) {
    if (!array_key_exists($agentName, $agentRegistry)) {
        $errors[] = "Capability matrix references unknown agent '{$agentName}'.";
    }

    foreach ($permissions as $toolName => $grants) {
        if (in_array($toolName, ['approval_required', 'denied_categories', 'denied_capabilities'], true)) {
            continue;
        }

        if (!array_key_exists($toolName, $toolRegistry)) {
            $errors[] = "Agent '{$agentName}' references unknown tool '{$toolName}'.";
            continue;
        }

        $knownCapabilities = [];
        foreach (($toolRegistry[$toolName]['capabilities'] ?? []) as $capabilityList) {
            foreach ((array) $capabilityList as $capability) {
                $knownCapabilities[$capability] = true;
            }
        }

        foreach ((array) $grants as $capability) {
            if (!isset($knownCapabilities[$capability])) {
                $errors[] = "Agent '{$agentName}' grants unknown capability '{$toolName}.{$capability}'.";
            }
        }
    }

    foreach ((array) ($permissions['approval_required'] ?? []) as $qualifiedCapability) {
        [$toolName, $capability] = array_pad(explode('.', $qualifiedCapability, 2), 2, null);
        if (!$toolName || !$capability || !isset($toolRegistry[$toolName])) {
            $errors[] = "Agent '{$agentName}' has invalid approval capability '{$qualifiedCapability}'.";
            continue;
        }

        $known = [];
        foreach (($toolRegistry[$toolName]['capabilities'] ?? []) as $capabilityList) {
            foreach ((array) $capabilityList as $item) {
                $known[$item] = true;
            }
        }
        if (!isset($known[$capability])) {
            $errors[] = "Agent '{$agentName}' requires approval for unknown capability '{$qualifiedCapability}'.";
        }
    }
}

foreach ($agentRegistry as $agentName => $definition) {
    if (!isset($matrixAgents[$agentName])) {
        $errors[] = "Agent '{$agentName}' has no capability matrix entry.";
    }
    if (empty($definition['role']) || empty($definition['mission'])) {
        $errors[] = "Agent '{$agentName}' must define role and mission.";
    }
}

$commands = $intents['commands'] ?? [];
foreach (['impl', 'fix', 'status', 'merge', 'review', 'deploy', 'investigate'] as $requiredIntent) {
    if (!isset($commands[$requiredIntent])) {
        $errors[] = "INTENT_MAP.yml is missing required intent '{$requiredIntent}'.";
    }
}

if ($errors !== []) {
    fwrite(STDERR, "Operating model validation failed:\n - " . implode("\n - ", $errors) . "\n");
    exit(1);
}

fwrite(STDOUT, sprintf(
    "Operating model valid: %d agents, %d tools, %d command intents.\n",
    count($agentRegistry),
    count($toolRegistry),
    count($commands),
));
