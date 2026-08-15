param(
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

$ownedSyntheticSeedPaths = @(
    'src/be/workslipapi/workslip.infrastructure/customerdata.csv'
)

function Test-CustomerExportPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $normalized = $Path.Replace('\\', '/').ToLowerInvariant()
    $fileName = [System.IO.Path]::GetFileName($normalized)
    $extension = [System.IO.Path]::GetExtension($fileName)

    if ($extension -notin @('.csv', '.xlsx', '.xls', '.json')) {
        return $false
    }

    # The current development customer seed is a verified synthetic fixture
    # (DEMO identifiers and .invalid email addresses) owned by DatabaseSeeder.
    # Keep the exception exact so another export with the same filename elsewhere is rejected.
    if ($ownedSyntheticSeedPaths -contains $normalized) {
        return $false
    }

    # Explicit synthetic/anonymised fixtures are allowed. The guard targets
    # repository exports, not legitimate test data with clearly non-production ownership.
    if ($normalized -match '(^|/)(tests?|testdata|fixtures?)/' -and
        $fileName -match '(synthetic|anonymi[sz]ed|fake|sample)') {
        return $false
    }

    $customerTerms = @('customer', 'customers', 'kunde', 'kunder', 'kundedata', 'persondata')
    $exportTerms = @('export', 'data', 'dump', 'extract', 'contacts', 'kontakt', 'crm')

    $hasCustomerTerm = $customerTerms | Where-Object { $fileName.Contains($_) } | Select-Object -First 1
    if (-not $hasCustomerTerm) {
        return $false
    }

    $hasExportTerm = $exportTerms | Where-Object { $fileName.Contains($_) } | Select-Object -First 1
    return [bool]$hasExportTerm -or $fileName -match '^(kunde|customer)s?[-_ ]'
}

if ($SelfTest) {
    $cases = @(
        @{ Path = 'customer-export.csv'; Expected = $true },
        @{ Path = 'exports/kundedata_2026.xlsx'; Expected = $true },
        @{ Path = 'tmp/customer_contacts.json'; Expected = $true },
        @{ Path = 'Docs/customerdata.csv'; Expected = $true },
        @{ Path = 'src/BE/WorkslipApi/Workslip.Infrastructure/customerdata.csv'; Expected = $false },
        @{ Path = 'other/customerdata.csv'; Expected = $true },
        @{ Path = 'tests/fixtures/synthetic-customer.json'; Expected = $false },
        @{ Path = 'tests/testdata/anonymized-kundedata.json'; Expected = $false },
        @{ Path = 'src/Customers/CustomerDataRow.cs'; Expected = $false },
        @{ Path = 'Docs/customer-migration.md'; Expected = $false }
    )

    foreach ($case in $cases) {
        $actual = Test-CustomerExportPath -Path $case.Path
        if ($actual -ne $case.Expected) {
            throw "Repository data hygiene self-test failed for '$($case.Path)'. Expected $($case.Expected), got $actual."
        }
    }

    Write-Host 'Repository data hygiene self-test passed.'
    exit 0
}

$trackedFiles = git ls-files
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate tracked files with git ls-files.'
}

$violations = @($trackedFiles | Where-Object { Test-CustomerExportPath -Path $_ })
if ($violations.Count -gt 0) {
    Write-Error ("Potential customer/person export artifacts are tracked:`n- " + ($violations -join "`n- "))
    exit 1
}

Write-Host 'Repository data hygiene check passed.'
