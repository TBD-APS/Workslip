<?php

declare(strict_types=1);

$root = dirname(__DIR__);
$buildDirectory = $root.'/build';
$outputFile = $buildDirectory.'/architecture.mmd';

if (!is_dir($buildDirectory) && !mkdir($buildDirectory, 0777, true) && !is_dir($buildDirectory)) {
    fwrite(STDERR, "Unable to create architecture build directory.\n");
    exit(1);
}

$command = sprintf(
    '%s analyse --config-file=%s --no-cache --formatter=mermaidjs --output=%s 2>&1',
    escapeshellarg($root.'/vendor/bin/deptrac'),
    escapeshellarg($root.'/deptrac.yaml'),
    escapeshellarg($outputFile),
);

$output = [];
$exitCode = 0;
exec($command, $output, $exitCode);

if ($exitCode !== 0 || !is_file($outputFile) || filesize($outputFile) === 0) {
    fwrite(STDERR, "Unable to render Deptrac architecture evidence.\n".implode(PHP_EOL, $output)."\n");
    exit(1);
}

fwrite(STDOUT, "Architecture evidence written to build/architecture.mmd.\n");
