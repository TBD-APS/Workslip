<?php

declare(strict_types=1);

use Symfony\Component\Yaml\Yaml;

require dirname(__DIR__) . '/vendor/autoload.php';

[$script, $agentName, $qualifiedCapability] = array_pad($argv, 3, null);
if (!$agentName || !$qualifiedCapability || !str_contains($qualifiedCapability, '.')) {
    fwrite(STDERR, "Usage: php scripts/resolve-agent-capability.php <agent> <tool.capability>\n");
    exit(2);
}

$repoRoot = dirname(__DIR__, 3);
$base = $repoRoot . '/Docs/operating-model';
$agents = Yaml::parseFile($base . '/agents/AGENT_REGISTRY.yml')['agents'] ?? [];
$matrix = Yaml::parseFile($base . '/agents/CAPABILITY_MATRIX.yml');
$tools = Yaml::parseFile($base . '/tools/TOOL_REGISTRY.yml')['tools'] ?? [];

if (!isset($agents[$agentName])) {
    emit(['decision' => 'deny', 'reason' => 'unknown_agent', 'agent' => $agentName], 3);
}

[$toolName, $capability] = explode('.', $qualifiedCapability, 2);
if (!isset($tools[$toolName])) {
    emit(['decision' => 'deny', 'reason' => 'unknown_tool', 'tool' => $toolName], 3);
}

$knownCapabilities = [];
foreach (($tools[$toolName]['capabilities'] ?? []) as $level => $items) {
    foreach ((array) $items as $item) {
        $knownCapabilities[$item] = $level;
    }
}
if (!isset($knownCapabilities[$capability])) {
    emit(['decision' => 'deny', 'reason' => 'unknown_capability', 'capability' => $qualifiedCapability], 3);
}

$permissions = $matrix['agents'][$agentName] ?? [];
$approvalRequired = array_flip((array) ($permissions['approval_required'] ?? []));
$explicitDenied = array_flip((array) ($permissions['denied_capabilities'] ?? []));
$deniedCategories = array_flip((array) ($permissions['denied_categories'] ?? []));
$category = $tools[$toolName]['category'] ?? null;

if (isset($explicitDenied[$qualifiedCapability]) || ($category && isset($deniedCategories[$category]))) {
    emit([
        'decision' => 'deny',
        'reason' => 'explicitly_denied',
        'agent' => $agentName,
        'capability' => $qualifiedCapability,
    ], 4);
}

$granted = in_array($capability, (array) ($permissions[$toolName] ?? []), true);
if (!$granted) {
    emit([
        'decision' => 'deny',
        'reason' => 'default_deny',
        'agent' => $agentName,
        'capability' => $qualifiedCapability,
    ], 4);
}

if (isset($approvalRequired[$qualifiedCapability])) {
    emit([
        'decision' => 'approval_required',
        'agent' => $agentName,
        'capability' => $qualifiedCapability,
        'risk' => $tools[$toolName]['risk'][$knownCapabilities[$capability]] ?? null,
    ]);
}

emit([
    'decision' => 'allow',
    'agent' => $agentName,
    'role' => $agents[$agentName]['role'] ?? null,
    'capability' => $qualifiedCapability,
    'risk' => $tools[$toolName]['risk'][$knownCapabilities[$capability]] ?? null,
]);

function emit(array $payload, int $exitCode = 0): never
{
    fwrite(STDOUT, json_encode($payload, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES) . PHP_EOL);
    exit($exitCode);
}
