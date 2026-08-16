<?php

declare(strict_types=1);

$root = dirname(__DIR__);
$command = sprintf(
    '%s %s --root=%s 2>&1',
    escapeshellarg(PHP_BINARY),
    escapeshellarg($root.'/scripts/forbid-ai-db-symbols.php'),
    escapeshellarg('tests/Architecture/Fixtures/ForbiddenSymbols'),
);

$output = [];
$exitCode = 0;
exec($command, $output, $exitCode);
$text = implode(PHP_EOL, $output);

if ($exitCode === 0) {
    fwrite(STDERR, "Expected forbidden DB-symbol fixtures to fail, but they passed.\n");
    exit(1);
}

$expectedEvidence = [
    'BadDbProvider.php',
    'Illuminate\\Support\\Facades\\DB',
    'BadPdoProvider.php',
    'raw PDO import/client',
    'BadAliasedPdoProvider.php',
    'BadCredentialProvider.php',
    'database environment credential access',
];

foreach ($expectedEvidence as $expected) {
    if (!str_contains($text, $expected)) {
        fwrite(STDERR, "DB-boundary fixtures failed without expected evidence: {$expected}.\n{$text}\n");
        exit(1);
    }
}

fwrite(STDOUT, "Forbidden DB facade, raw/aliased PDO and DB credential fixtures failed for the intended reasons.\n");
