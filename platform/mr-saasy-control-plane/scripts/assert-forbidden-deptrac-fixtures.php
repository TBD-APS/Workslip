<?php

declare(strict_types=1);

$root = dirname(__DIR__);
$command = sprintf(
    '%s analyse --config-file=%s --no-cache --formatter=table 2>&1',
    escapeshellarg($root.'/vendor/bin/deptrac'),
    escapeshellarg($root.'/tests/Architecture/deptrac.forbidden.yaml'),
);

$output = [];
$exitCode = 0;
exec($command, $output, $exitCode);
$text = implode(PHP_EOL, $output);

if ($exitCode === 0) {
    fwrite(STDERR, "Expected forbidden Deptrac fixture to fail, but it passed.\n");
    exit(1);
}

foreach (['BadProvider', 'BadAgent'] as $expectedViolation) {
    if (!str_contains($text, $expectedViolation)) {
        fwrite(STDERR, "Forbidden fixture failed without expected {$expectedViolation} violation.\n{$text}\n");
        exit(1);
    }
}

fwrite(STDOUT, "Forbidden Deptrac fixtures failed for the intended provider/application -> persistence dependencies.\n");
