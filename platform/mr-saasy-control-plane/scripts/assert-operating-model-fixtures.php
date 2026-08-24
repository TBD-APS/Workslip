<?php

declare(strict_types=1);

$script = __DIR__ . '/resolve-agent-capability.php';

$cases = [
    ['grok', 'github.repository_read', 0, 'allow'],
    ['grok', 'github.code_write', 4, 'deny'],
    ['grok', 'database.schema_read', 4, 'deny'],
    ['codex', 'github.code_write', 0, 'allow'],
    ['codex', 'github.merge', 4, 'deny'],
    ['visual_qa', 'github.code_write', 4, 'deny'],
    ['security', 'database.schema_read', 0, 'allow'],
    ['devops', 'azure.deployment', 0, 'allow'],
    ['devops', 'azure.resource_delete', 4, 'deny'],
];

foreach ($cases as [$agent, $capability, $expectedExit, $expectedDecision]) {
    $command = sprintf(
        '%s %s %s %s',
        escapeshellarg(PHP_BINARY),
        escapeshellarg($script),
        escapeshellarg($agent),
        escapeshellarg($capability),
    );

    exec($command, $lines, $exitCode);
    $payload = json_decode(implode("\n", $lines), true);
    $decision = $payload['decision'] ?? null;

    if ($exitCode !== $expectedExit || $decision !== $expectedDecision) {
        fwrite(STDERR, sprintf(
            "Operating model fixture failed for %s %s: expected exit=%d decision=%s, got exit=%d decision=%s\n",
            $agent,
            $capability,
            $expectedExit,
            $expectedDecision,
            $exitCode,
            var_export($decision, true),
        ));
        exit(1);
    }

    $lines = [];
}

fwrite(STDOUT, sprintf("Operating model permission fixtures passed: %d cases.\n", count($cases)));
