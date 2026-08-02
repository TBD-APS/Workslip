[CmdletBinding()]
param(
    [ValidateSet(
        'public-smoke',
        'auth-session',
        'kls-lifecycle',
        'rejection-loop',
        'draft-recovery',
        'role-tenant-isolation',
        'invitation-onboarding',
        'assignment-lifecycle',
        'customer-lifecycle',
        'worksheet-integrity',
        'diverse-lifecycle',
        'all-critical'
    )]
    [string]$Scenario = 'public-smoke',

    [ValidateSet('Direct', 'Workflow')]
    [string]$Mode = 'Direct',

    [string]$AppUrl = 'https://app.mrsoftware.dk',

    [switch]$SkipBrowserInstall,

    [switch]$NoOpenEvidence
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$playwrightVersion = '1.55.0'
$playwrightImage = "mcr.microsoft.com/playwright:v$playwrightVersion-noble"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$frontendRoot = Join-Path $repoRoot 'src\FE'
$artifactRoot = Join-Path $repoRoot 'artifacts\playwright-prod-smoke'
$reportPath = Join-Path $artifactRoot 'report.json'
$workflowPath = '.github/workflows/playwright-prod-smoke.yml'

function Assert-Command {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string]$InstallHint
    )

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Kommandoen '$Name' blev ikke fundet. $InstallHint"
    }
}

function Invoke-External {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Command,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,

        [Parameter(Mandatory = $true)]
        [string]$WorkingDirectory
    )

    Push-Location $WorkingDirectory
    try {
        Write-Host "> $Command $($Arguments -join ' ')" -ForegroundColor DarkGray
        & $Command @Arguments
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            throw "Kommandoen fejlede med exit code ${exitCode}: $Command $($Arguments -join ' ')"
        }
    }
    finally {
        Pop-Location
    }
}

function Reset-Evidence {
    if (Test-Path $artifactRoot) {
        Remove-Item $artifactRoot -Recurse -Force
    }
}

function Open-Evidence {
    if ($NoOpenEvidence) {
        return
    }

    if (Test-Path $artifactRoot) {
        Write-Host "Ã…bner evidence-mappen: $artifactRoot" -ForegroundColor Green
        Invoke-Item $artifactRoot
    }
    else {
        Write-Warning "Der blev ikke oprettet en evidence-mappe pÃ¥ $artifactRoot."
    }
}

function Invoke-DirectRun {
    Assert-Command -Name 'node' -InstallHint 'InstallÃ©r Node.js 22 LTS, eksempelvis: winget install OpenJS.NodeJS.LTS'
    Assert-Command -Name 'npm' -InstallHint 'npm fÃ¸lger med Node.js.'

    $nodeVersion = (& node --version).Trim().TrimStart([char]'v')
    $nodeMajor = [int]($nodeVersion.Split('.')[0])
    if ($nodeMajor -lt 20) {
        throw "Node.js $nodeVersion er for gammel. Brug Node.js 20 eller nyere; Node.js 22 anbefales."
    }

    $playwrightPackageJson = Join-Path $frontendRoot 'scripts\node_modules\playwright\package.json'
    $installedVersion = $null
    if (Test-Path $playwrightPackageJson) {
        try {
            $installedVersion = (Get-Content $playwrightPackageJson -Raw | ConvertFrom-Json).version
        }
        catch {
            $installedVersion = $null
        }
    }

    if ($installedVersion -ne $playwrightVersion) {
        Write-Host "Installerer den isolerede Playwright-runtime $playwrightVersion..." -ForegroundColor Cyan
        Invoke-External -Command 'npm' -Arguments @(
            'install',
            '--prefix', 'scripts',
            '--no-save',
            '--package-lock=false',
            '--ignore-scripts',
            '--no-audit',
            '--no-fund',
            "playwright@$playwrightVersion"
        ) -WorkingDirectory $frontendRoot
    }
    else {
        Write-Host "Playwright-runtime $playwrightVersion er allerede installeret." -ForegroundColor DarkGreen
    }

    if (-not $SkipBrowserInstall) {
        Write-Host 'Kontrollerer lokal Chromium-installation...' -ForegroundColor Cyan
        Invoke-External -Command 'node' -Arguments @(
            'scripts/node_modules/playwright/cli.js',
            'install',
            'chromium'
        ) -WorkingDirectory $frontendRoot
    }

    $sourceFiles = @(
        'scripts/playwright-prod-smoke.mjs',
        'scripts/playwright-critical-contract.mjs',
        'scripts/playwright-critical-domain.mjs',
        'scripts/playwright-scenarios-core.mjs',
        'scripts/playwright-scenarios-admin.mjs'
    )

    Write-Host 'Validerer Playwright-kilder og Postman collection...' -ForegroundColor Cyan
    foreach ($sourceFile in $sourceFiles) {
        Invoke-External -Command 'node' -Arguments @('--check', $sourceFile) -WorkingDirectory $frontendRoot
    }

    Invoke-External -Command 'node' -Arguments @(
        '-e',
        "JSON.parse(require('node:fs').readFileSync('../BE/WorkslipApi/Postman/postman_collection.json', 'utf8')); console.log('Postman collection JSON OK');"
    ) -WorkingDirectory $frontendRoot

    $previousProdUrl = $env:PROD_URL
    $previousScenario = $env:SCENARIO

    try {
        $env:PROD_URL = $AppUrl.TrimEnd('/')
        $env:SCENARIO = $Scenario

        Write-Host "KÃ¸rer '$Scenario' direkte mod $($env:PROD_URL)..." -ForegroundColor Cyan
        Invoke-External -Command 'node' -Arguments @('scripts/playwright-prod-smoke.mjs') -WorkingDirectory $frontendRoot
    }
    finally {
        if ($null -eq $previousProdUrl) {
            Remove-Item Env:PROD_URL -ErrorAction SilentlyContinue
        }
        else {
            $env:PROD_URL = $previousProdUrl
        }

        if ($null -eq $previousScenario) {
            Remove-Item Env:SCENARIO -ErrorAction SilentlyContinue
        }
        else {
            $env:SCENARIO = $previousScenario
        }
    }
}

function Invoke-WorkflowRun {
    Assert-Command -Name 'docker' -InstallHint 'InstallÃ©r og start Docker Desktop: winget install Docker.DockerDesktop'
    Assert-Command -Name 'act' -InstallHint 'InstallÃ©r act: winget install nektos.act'

    Write-Host 'Kontrollerer Docker Desktop...' -ForegroundColor Cyan
    Invoke-External -Command 'docker' -Arguments @('info') -WorkingDirectory $repoRoot

    & docker image inspect $playwrightImage *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Henter Playwright-image fÃ¸rste gang: $playwrightImage" -ForegroundColor Cyan
        Invoke-External -Command 'docker' -Arguments @('pull', $playwrightImage) -WorkingDirectory $repoRoot
    }
    else {
        Write-Host 'Playwright Docker-image findes allerede lokalt.' -ForegroundColor DarkGreen
    }

    if ($Scenario -eq 'all-critical') {
        Write-Warning 'Workflow-mode starter alle ti matrix-jobs. Test public-smoke eller Ã©t kritisk flow fÃ¸rst.'
    }

    $eventPath = Join-Path ([System.IO.Path]::GetTempPath()) ("workslip-playwright-{0}.json" -f [Guid]::NewGuid())
    $eventJson = @{
        inputs = @{
            scenario = $Scenario
        }
    } | ConvertTo-Json -Depth 4

    $utf8NoBom = New-Object System.Text.UTF8Encoding -ArgumentList $false
    [System.IO.File]::WriteAllText($eventPath, $eventJson, $utf8NoBom)

    try {
        Write-Host "KÃ¸rer den faktiske GitHub Actions-YAML lokalt med act: $Scenario" -ForegroundColor Cyan
        Invoke-External -Command 'act' -Arguments @(
            'workflow_dispatch',
            '--workflows', $workflowPath,
            '--eventpath', $eventPath,
            '--job', 'smoke',
            '--platform', "ubuntu-latest=$playwrightImage",
            '--pull=false',
            '--bind'
        ) -WorkingDirectory $repoRoot

        if (-not (Test-Path $reportPath)) {
            throw "Workflowet lykkedes, men report.json blev ikke gemt på værten: $reportPath"
        }
    }
    finally {
        Remove-Item $eventPath -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Workslip Playwright local runner - mode: $Mode, scenario: $Scenario" -ForegroundColor White
Reset-Evidence

try {
    if ($Mode -eq 'Workflow') {
        Invoke-WorkflowRun
    }
    else {
        Invoke-DirectRun
    }
}
finally {
    Open-Evidence
}

