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
    'Illuminate\\Support\\Facades\\Schema',
    'Illuminate\\Database\\Eloquent\\Model',
    'Illuminate\\Database\\Query\\Builder',
    'Illuminate\\Database\\Eloquent\\Builder',
    'Illuminate\\Database\\Connection',
    'Illuminate\\Database\\DatabaseManager',
    'Illuminate\\Database\\Capsule\\Manager',
    'App\\Infrastructure\\Persistence',
    'App\\ProductAdapters\\Workslip',
];

$dbCredentialNames = '(?:DB_(?:CONNECTION|HOST|PORT|DATABASE|USERNAME|PASSWORD)|DATABASE_URL)';

$forbiddenPatterns = [
    'raw PDO import/client' => '/(?:\\buse\\s+\\\\?PDO(?:\\s+as\\s+[A-Za-z_][A-Za-z0-9_]*)?\\s*;|\\bnew\\s+\\\\?PDO\\s*\\()/i',
    'raw mysqli import/client' => '/(?:\\buse\\s+\\\\?mysqli(?:\\s+as\\s+[A-Za-z_][A-Za-z0-9_]*)?\\s*;|\\bnew\\s+\\\\?mysqli\\s*\\()/i',
    'Laravel database config access' => '/\\bconfig\\s*\\(\\s*[\'\"]database(?:\\.|[\'\"])/i',
    'database environment credential access' => '/\\benv\\s*\\(\\s*[\'\"]'.$dbCredentialNames.'[\'\"]/i',
    'getenv database credential access' => '/\\bgetenv\\s*\\(\\s*[\'\"]'.$dbCredentialNames.'[\'\"]/i',
    'direct database environment lookup' => '/[\'\"]'.$dbCredentialNames.'[\'\"]\\s*\\]/i',
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

        $relativeFile = ltrim(str_replace($projectRoot, '', $file->getPathname()), DIRECTORY_SEPARATOR);

        foreach ($forbiddenSymbols as $symbol) {
            if (str_contains($contents, $symbol)) {
                $violations[] = "{$relativeFile}: forbidden AI/provider dependency {$symbol}";
            }
        }

        foreach ($forbiddenPatterns as $label => $pattern) {
            if (preg_match($pattern, $contents) === 1) {
                $violations[] = "{$relativeFile}: forbidden AI/provider database path {$label}";
            }
        }
    }
}

if ($violations !== []) {
    fwrite(STDERR, "AI/provider database boundary violation(s):\n- ".implode("\n- ", $violations)."\n");
    exit(1);
}

fwrite(STDOUT, "AI/provider direct DB/Eloquent/persistence/credential paths: none.\n");
