[CmdletBinding()]
param(
    [string]$ReportUrl,
    [switch]$SkipInstall,
    [switch]$NoBrowser
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$backendTests = Join-Path $repoRoot 'src/BE/WorkslipApi/Workslip.Tests/Workslip.Tests.csproj'
$frontendRoot = Join-Path $repoRoot 'src/FE'
$docsCheck = Join-Path $repoRoot 'tools/docs/check_docs.py'
$devScript = Join-Path $repoRoot 'dev.ps1'
$expectedBranch = 'rbj--542-power-bi-embedded-analytics'
$backendUrl = 'http://localhost:5262'
$frontendUrl = 'http://127.0.0.1:5270'
$timerUrl = "$frontendUrl/app/timer"
$adminEmail = 'admin@17v3ygzs.mailosaur.net'
$userEmail = 'user@17v3ygzs.mailosaur.net'

function Write-Step([string]$Message) {
    Write-Host "[....] $Message" -ForegroundColor Cyan
}

function Write-Ok([string]$Message) {
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Invoke-Checked([string]$Name, [scriptblock]$Action) {
    Write-Step $Name
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }
    Write-Ok $Name
}

function Get-RequiredCommand([string]$Name, [string]$Hint) {
    $command = Get-Command $Name -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $command) {
        throw "$Name is required. $Hint"
    }
    return $command.Source
}

function Get-DevToken([string]$Email) {
    return Invoke-RestMethod `
        -Method Post `
        -Uri "$backendUrl/api/dev/token" `
        -ContentType 'application/json' `
        -Body (@{ email = $Email } | ConvertTo-Json)
}

function Invoke-ExpectedForbidden([string]$Url, [hashtable]$Headers) {
    try {
        Invoke-WebRequest -Method Get -Uri $Url -Headers $Headers -UseBasicParsing | Out-Null
        throw 'Expected HTTP 403 for a non-Admin user, but the endpoint returned success.'
    }
    catch {
        $statusCode = $null
        if ($null -ne $_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }

        if ($statusCode -ne 403) {
            throw
        }
    }
}

$git = Get-RequiredCommand 'git' 'Install Git for Windows.'
$dotnet = Get-RequiredCommand 'dotnet' 'Install the .NET 10 SDK.'
$npmResolved = Get-Command 'npm.cmd' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $npmResolved) {
    throw 'npm.cmd is required. Install Node.js 24 for Windows.'
}
$npm = $npmResolved.Source
$python = Get-RequiredCommand 'python' 'Install Python 3 for the maintained documentation check.'

$currentBranch = (& $git -C $repoRoot branch --show-current).Trim()
if ($LASTEXITCODE -ne 0) {
    throw 'Could not determine the current Git branch.'
}
if ($currentBranch -ne $expectedBranch) {
    throw "WOR-542 local validation must run from '$expectedBranch'. Current branch: '$currentBranch'."
}
Write-Ok "Branch $currentBranch"

$status = @(& $git -C $repoRoot status --porcelain)
if ($LASTEXITCODE -ne 0) {
    throw 'Could not inspect Git worktree status.'
}
if ($status.Count -gt 0) {
    throw "WOR-542 validation requires a clean worktree so results map to one exact branch state.`n$($status -join [Environment]::NewLine)"
}
Write-Ok 'Clean worktree'

if (-not [string]::IsNullOrWhiteSpace($ReportUrl)) {
    $parsedReportUrl = $null
    if (-not [Uri]::TryCreate($ReportUrl.Trim(), [UriKind]::Absolute, [ref]$parsedReportUrl)
        -or $parsedReportUrl.Scheme -ne 'https'
        -or -not $parsedReportUrl.IsDefaultPort
        -or -not [string]::Equals($parsedReportUrl.Host, 'app.powerbi.com', [StringComparison]::OrdinalIgnoreCase)
        -or $parsedReportUrl.AbsolutePath.StartsWith('/view', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'ReportUrl must be a normal authenticated https://app.powerbi.com report URL. Publish-to-web /view links are not allowed.'
    }
}

if (-not $SkipInstall) {
    Invoke-Checked 'Frontend npm ci' {
        Push-Location $frontendRoot
        try { & $npm ci } finally { Pop-Location }
    }
}

Invoke-Checked 'WOR-542 backend URL resolver tests' {
    & $dotnet test $backendTests -c Release --nologo --filter 'FullyQualifiedName~PowerBiReportUrlResolverTests'
}

Invoke-Checked 'WOR-542 frontend component tests' {
    Push-Location $frontendRoot
    try {
        & $npm test -- --run 'src/features/worksheets/components/AdminHoursExport.preview.test.tsx'
    }
    finally {
        Pop-Location
    }
}

Invoke-Checked 'Frontend production build' {
    Push-Location $frontendRoot
    try { & $npm run build } finally { Pop-Location }
}

Invoke-Checked 'Maintained documentation check' {
    & $python $docsCheck
}

$previousReportUrl = $env:PowerBiReport__Url
try {
    if ([string]::IsNullOrWhiteSpace($ReportUrl)) {
        Remove-Item Env:PowerBiReport__Url -ErrorAction SilentlyContinue
        Write-Step 'Starting Workslip in WOR-542 no-configuration mode'
    }
    else {
        $env:PowerBiReport__Url = $ReportUrl.Trim()
        Write-Step 'Starting Workslip with the supplied authenticated Power BI report URL'
    }

    & $devScript -SkipInstall -NoBrowser
    if ($LASTEXITCODE -ne 0) {
        throw "dev.ps1 failed with exit code $LASTEXITCODE."
    }

    Write-Step 'Verifying Admin Power BI report endpoint'
    $adminToken = Get-DevToken $adminEmail
    if ([string]::IsNullOrWhiteSpace([string]$adminToken.token)) {
        throw 'Synthetic Admin token was not returned.'
    }
    $adminHeaders = @{ Authorization = "Bearer $($adminToken.token)" }
    $report = Invoke-RestMethod `
        -Method Get `
        -Uri "$backendUrl/api/worksheets/all/report/power-bi" `
        -Headers $adminHeaders

    if ([string]::IsNullOrWhiteSpace($ReportUrl)) {
        if ($null -ne $report.url -or $null -ne $report.embedUrl) {
            throw 'No-config validation expected null url/embedUrl from the Admin endpoint.'
        }
        Write-Ok 'Admin endpoint returns the expected no-config state'
    }
    else {
        if ([string]::IsNullOrWhiteSpace([string]$report.url)
            -or [string]::IsNullOrWhiteSpace([string]$report.embedUrl)
            -or -not ([string]$report.embedUrl).StartsWith('https://app.powerbi.com/reportEmbed?', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Configured-report validation did not return a secure Power BI embed URL.'
        }
        Write-Ok 'Admin endpoint returns a secure Power BI embed URL'
    }

    Write-Step 'Verifying non-Admin cannot read Power BI report configuration'
    $userToken = Get-DevToken $userEmail
    if ([string]::IsNullOrWhiteSpace([string]$userToken.token)) {
        throw 'Synthetic User token was not returned.'
    }
    Invoke-ExpectedForbidden `
        "$backendUrl/api/worksheets/all/report/power-bi" `
        @{ Authorization = "Bearer $($userToken.token)" }
    Write-Ok 'Non-Admin is rejected with HTTP 403'

    Write-Host ''
    Write-Host 'WOR-542 local automated checks passed.' -ForegroundColor Green
    Write-Host "  Timer:   $timerUrl"
    if ([string]::IsNullOrWhiteSpace($ReportUrl)) {
        Write-Host '  Mode:    no Power BI report configured'
        Write-Host '  Expect:  Power BI-overblik -> Power BI er ikke konfigureret endnu'
    }
    else {
        Write-Host '  Mode:    authenticated Power BI report configured'
        Write-Host '  Expect:  Power BI-overblik contains the report iframe and Åbn i Power BI fallback'
        Write-Host '  Sign-in: use the intended Microsoft organizational account that owns/has access to the report'
    }
    Write-Host ''
    Write-Host 'Manual browser acceptance still required before review:' -ForegroundColor Yellow
    Write-Host '  1. Log in with Dev Login · Admin.'
    Write-Host '  2. Open Timer and verify the visible Power BI state.'
    Write-Host '  3. If ReportUrl was supplied, verify Microsoft sign-in, report rendering and slicers/filters.'
    Write-Host '  4. Verify a narrow/mobile viewport is usable.'
    Write-Host '  5. Do not send to review until the product owner explicitly writes: send til review.'

    if (-not $NoBrowser) {
        Start-Process $timerUrl
    }
}
finally {
    if ($null -eq $previousReportUrl) {
        Remove-Item Env:PowerBiReport__Url -ErrorAction SilentlyContinue
    }
    else {
        $env:PowerBiReport__Url = $previousReportUrl
    }
}
