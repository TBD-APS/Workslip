<?php

declare(strict_types=1);

$projectRoot = dirname(__DIR__);
$requestedRoot = null;

foreach (array_slice($argv, 1) as $argument) {
    if (str_starts_with($argument, '--root=')) {
        $requestedRoot = substr($argument, strlen('--root='));
    }
}

$scanRoots = $requestedRoot !== null
    ? [$requestedRoot]
    : [
        'app/AI/Application',
        'app/AI/Agents',
        'app/AI/Providers',
    ];

$forbiddenSymbols = [
    'Illuminate\\Support\\Facades\\DB',
    'Illuminate\\Database\\Eloquent\\Model',
    'Illuminate\\Database\\Query\\Builder',
    'Illuminate\\Database\\Eloquent\\Builder',
    'App\\Infrastructure\\Persistence',
    'App\\ProductAdapters\\Workslip',
];

$violations = [];

foreach ($scanRoots as $relativeRoot) {
    $absoluteRoot = $projectRoot.'/'.ltrim($relativeRoot, '/');
    if (!is_dir($absoluteRoot)) {
        continue;
    }

    $iterator = new RecursiveIteratorIterator(
        new RecursiveDirectoryIterator($absoluteRoot, FilesystemIterator::SKIP_DOTS),
    );

    foreach ($iterator as $file) {
        if (!$file instanceof SplFileInfo || !$file->isFile() || $file->getExtension() !== 'php') {
            continue;
        }

        $contents = file_get_contents($file->getPathname());
        if ($contents === false) {
            fwrite(STDERR, "Unable to read {$file->getPathname()}.\n");
            exit(1);
        }

        foreach ($forbiddenSymbols as $symbol) {
            if (str_contains($contents, $symbol)) {
                $relativeFile = ltrim(str_replace($projectRoot, '', $file->getPathname()), DIRECTORY_SEPARATOR);
                $violations[] = "{$relativeFile}: forbidden AI/provider dependency {$symbol}";
            }
        }
    }
}

if ($violations !== []) {
    fwrite(STDERR, "AI/provider database boundary violation(s):\n- ".implode("\n- ", $violations)."\n");
    exit(1);
}

fwrite(STDOUT, "AI/provider direct DB/Eloquent/persistence symbols: none.\n");
