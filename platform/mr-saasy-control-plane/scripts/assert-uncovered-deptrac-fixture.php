<?php

declare(strict_types=1);

$root = dirname(__DIR__);
$command = sprintf(
    '%s analyse --config-file=%s --no-cache --fail-on-uncovered --formatter=table 2>&1',
    escapeshellarg($root.'/vendor/bin/deptrac'),
    escapeshellarg($root.'/tests/Architecture/deptrac.uncovered.yaml'),
);

$output = [];
$exitCode = 0;
exec($command, $output, $exitCode);
$text = implode(PHP_EOL, $output);

if ($exitCode === 0) {
    fwrite(STDERR, "Expected uncovered dependency fixture to fail, but it passed.\n");
    exit(1);
}

if (!str_contains($text, 'Uncovered') || !str_contains($text, 'DbBridge')) {
    fwrite(STDERR, "Uncovered fixture failed without the expected DbBridge uncovered dependency.\n{$text}\n");
    exit(1);
}

fwrite(STDOUT, "Uncovered dependency fixture failed for the intended unclassified bridge dependency.\n");
