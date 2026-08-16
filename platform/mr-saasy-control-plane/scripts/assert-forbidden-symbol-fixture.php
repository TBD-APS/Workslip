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
    fwrite(STDERR, "Expected forbidden DB-symbol fixture to fail, but it passed.\n");
    exit(1);
}

if (!str_contains($text, 'BadDbProvider.php') || !str_contains($text, 'Illuminate\\Support\\Facades\\DB')) {
    fwrite(STDERR, "DB-symbol fixture failed without the intended DB facade evidence.\n{$text}\n");
    exit(1);
}

fwrite(STDOUT, "Forbidden direct DB symbol fixture failed for the intended reason.\n");
