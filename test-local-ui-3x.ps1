[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$frontendRoot = Join-Path $repoRoot 'src/FE'
$playwrightVersion = '1.55.0'
$playwrightPackageJson = Join-Path $frontendRoot 'scripts/node_modules/playwright/package.json'

function Assert-Url([string]$Url, [string]$Name) {
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3
        if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 500) { throw "$Name returned HTTP $($response.StatusCode)." }
    } catch { throw "$Name is not ready at $Url. Start Workslip first with .\start-local.cmd. $($_.Exception.Message)" }
}

Write-Host 'Workslip local UI QA - 3 passes' -ForegroundColor White
Write-Host '-----------------------------------' -ForegroundColor DarkGray
Assert-Url 'http://localhost:5262/health' 'Backend'
Assert-Url 'http://127.0.0.1:5270' 'Frontend'
Write-Host '[OK] Local backend and frontend are ready' -ForegroundColor Green

$installedVersion = $null
if (Test-Path $playwrightPackageJson) { try { $installedVersion = (Get-Content $playwrightPackageJson -Raw | ConvertFrom-Json).version } catch { $installedVersion = $null } }

Push-Location $frontendRoot
try {
    if ($installedVersion -ne $playwrightVersion) {
        & npm.cmd install --prefix scripts --no-save --package-lock=false --ignore-scripts --no-audit --no-fund "playwright@$playwrightVersion"
        if ($LASTEXITCODE -ne 0) { throw 'Playwright runtime install failed.' }
    }
    & node scripts/node_modules/playwright/cli.js install chromium
    if ($LASTEXITCODE -ne 0) { throw 'Playwright Chromium install failed.' }
    & npm.cmd run lint
    if ($LASTEXITCODE -ne 0) { throw 'Frontend lint failed.' }
    for ($round = 1; $round -le 3; $round++) {
        Write-Host "[....] Focused UI tests $round/3" -ForegroundColor Cyan
        & npm.cmd test -- --run src/features/overview/routes/Overview.test.tsx src/components/common/quickNavigatorSearch.test.ts src/providers/roleDestination.test.ts
        if ($LASTEXITCODE -ne 0) { throw "Focused frontend tests failed in round $round." }
    }
    & npm.cmd run build:local
    if ($LASTEXITCODE -ne 0) { throw 'Frontend local build failed.' }
    $env:WORKSLIP_QA_ROUNDS = '3'
    try {
        & node scripts/playwright-local-ui-qa.mjs
        if ($LASTEXITCODE -ne 0) { throw 'Local browser QA failed.' }
    } finally { Remove-Item Env:WORKSLIP_QA_ROUNDS -ErrorAction SilentlyContinue }
} finally { Pop-Location }

Write-Host '[OK] Local UI QA completed successfully.' -ForegroundColor Green
Write-Host 'Evidence: src/FE/artifacts/local-ui-qa'
