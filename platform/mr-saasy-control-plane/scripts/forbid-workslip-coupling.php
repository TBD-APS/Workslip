<?php

declare(strict_types=1);

$projectRoot = dirname(__DIR__);
$scanRoots = ['app', 'bootstrap', 'routes'];
$forbiddenFragments = [
    'Workslip\\',
    'WorkslipApi',
    'src/BE/WorkslipApi',
    'src/FE',
];
$violations = [];

foreach ($scanRoots as $relativeRoot) {
    $absoluteRoot = $projectRoot.'/'.$relativeRoot;
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

        foreach ($forbiddenFragments as $fragment) {
            if (str_contains($contents, $fragment)) {
                $relativeFile = ltrim(str_replace($projectRoot, '', $file->getPathname()), DIRECTORY_SEPARATOR);
                $violations[] = "{$relativeFile}: forbidden Workslip coupling {$fragment}";
            }
        }
    }
}

if ($violations !== []) {
    fwrite(STDERR, "Control-plane product coupling violation(s):\n- ".implode("\n- ", $violations)."\n");
    exit(1);
}

fwrite(STDOUT, "Control-plane direct Workslip source coupling: none.\n");
