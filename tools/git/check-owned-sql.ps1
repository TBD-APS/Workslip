param(
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

$ownedSqlRoots = @(
    'src/BE/infrastructure/database/migrations/',
    'src/BE/infrastructure/operations/'
)

function Test-OwnedSqlPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $normalized = $Path.Replace('\\', '/')
    if (-not $normalized.EndsWith('.sql', [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    foreach ($root in $ownedSqlRoots) {
        if ($normalized.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

if ($SelfTest) {
    $cases = @(
        @{ Path = 'src/BE/infrastructure/database/migrations/20260815_1200_example.sql'; Expected = $true },
        @{ Path = 'src/BE/infrastructure/operations/approved-maintenance.sql'; Expected = $true },
        @{ Path = 'Sql queries/drop-everything.sql'; Expected = $false },
        @{ Path = 'tmp/prod-debug.sql'; Expected = $false },
        @{ Path = 'Docs/database.md'; Expected = $true }
    )

    foreach ($case in $cases) {
        $actual = Test-OwnedSqlPath -Path $case.Path
        if ($actual -ne $case.Expected) {
            throw "Owned SQL self-test failed for '$($case.Path)'. Expected $($case.Expected), got $actual."
        }
    }

    Write-Host 'Owned SQL self-test passed.'
    exit 0
}

$trackedSql = @(git ls-files '*.sql')
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate tracked SQL files with git ls-files.'
}

$violations = @($trackedSql | Where-Object { -not (Test-OwnedSqlPath -Path $_) })
if ($violations.Count -gt 0) {
    Write-Error ("SQL files must live under an explicitly owned migrations/operations path:`n- " + ($violations -join "`n- "))
    exit 1
}

Write-Host 'Owned SQL path check passed.'
