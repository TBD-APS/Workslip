[CmdletBinding()]
param(
    [switch]$SkipInstall,
    [switch]$NoBrowser,
    [switch]$CheckOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$backendPath = Join-Path $repoRoot 'src/BE/WorkslipApi'
$backendProject = Join-Path $backendPath 'Workslip.Api.csproj'
$frontendPath = Join-Path $repoRoot 'src/FE'
$backendUrl = 'http://localhost:5262'
$frontendUrl = 'http://127.0.0.1:5270'
$devEmail = 'admin@17v3ygzs.mailosaur.net'

function Write-Ok([string]$Message) {
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Write-Step([string]$Message) {
    Write-Host "[....] $Message" -ForegroundColor Cyan
}

function Assert-Command([string]$Name, [string]$InstallHint) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name is required. $InstallHint"
    }
}

function Assert-MajorVersion([string]$Name, [string]$RawVersion, [int]$MinimumMajor) {
    $normalized = $RawVersion.Trim().TrimStart('v')
    $majorText = ($normalized -split '\.')[0]
    $major = 0
    if (-not [int]::TryParse($majorText, [ref]$major) -or $major -lt $MinimumMajor) {
        throw "$Name $MinimumMajor+ is required. Found '$RawVersion'."
    }
}

function Test-Url([string]$Url) {
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 2
        return $response.StatusCode -ge 200 -and $response.StatusCode -lt 500
    }
    catch {
        return $false
    }
}

function Wait-ForUrl([string]$Url, [int]$TimeoutSeconds, [System.Diagnostics.Process]$Process, [string]$Name, [string]$LogPath) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (Test-Url $Url) {
            return
        }

        if ($Process.HasExited) {
            $tail = if (Test-Path $LogPath) { (Get-Content $LogPath -Tail 40) -join [Environment]::NewLine } else { '<no log>' }
            throw "$Name exited before becoming ready.`n$tail"
        }

        Start-Sleep -Milliseconds 500
    }

    $logTail = if (Test-Path $LogPath) { (Get-Content $LogPath -Tail 40) -join [Environment]::NewLine } else { '<no log>' }
    throw "$Name did not become ready at $Url within $TimeoutSeconds seconds.`n$logTail"
}

function Assert-PortFree([int]$Port, [string]$ServiceName) {
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
    if ($listeners.Count -gt 0) {
        throw "Port $Port is already in use. Stop the existing $ServiceName/local process before running dev.ps1."
    }
}

function New-EphemeralSigningKey {
    $bytes = New-Object byte[] 48
    [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToBase64String($bytes)
}

if (-not $IsWindows) {
    throw 'dev.ps1 currently supports the canonical Windows developer path only.'
}

Write-Host 'Workslip Local Dev Doctor' -ForegroundColor White
Write-Host '-------------------------' -ForegroundColor DarkGray

Assert-Command 'dotnet' 'Install the .NET 10 SDK.'
Assert-Command 'node' 'Install Node.js 24 LTS/current as documented by Workslip.'
Assert-Command 'npm' 'npm is installed with Node.js.'
Assert-Command 'sqllocaldb' 'Install SQL Server LocalDB (normally included with Visual Studio SQL tooling).'

$dotnetVersion = (& dotnet --version)
$nodeVersion = (& node --version)
$npmVersion = (& npm --version)
Assert-MajorVersion '.NET SDK' $dotnetVersion 10
Assert-MajorVersion 'Node.js' $nodeVersion 24
Write-Ok ".NET SDK $dotnetVersion"
Write-Ok "Node.js $nodeVersion / npm $npmVersion"

$localDbInstances = @(& sqllocaldb info 2>$null)
if ($localDbInstances -notcontains 'MSSQLLocalDB') {
    Write-Step 'Creating local MSSQLLocalDB instance'
    & sqllocaldb create MSSQLLocalDB | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create MSSQLLocalDB.' }
}
& sqllocaldb start MSSQLLocalDB | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Could not start MSSQLLocalDB.' }
Write-Ok 'SQL Server LocalDB MSSQLLocalDB'

if (-not (Test-Path $backendProject)) { throw "Backend project not found: $backendProject" }
if (-not (Test-Path (Join-Path $frontendPath 'package-lock.json'))) { throw 'Frontend package-lock.json is missing.' }
if (-not (Test-Path (Join-Path $backendPath 'appsettings.Development.json'))) { throw 'Tracked appsettings.Development.json is missing.' }
Write-Ok 'Tracked Development configuration present'

if ($CheckOnly) {
    Write-Host ''
    Write-Host 'Doctor checks passed. No processes or dependencies were changed because -CheckOnly was used.' -ForegroundColor Green
    exit 0
}

Assert-PortFree 5262 'backend'
Assert-PortFree 5270 'frontend'

if (-not $SkipInstall) {
    Write-Step 'Restoring backend dependencies'
    & dotnet restore $backendProject --nologo
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
    Write-Ok 'Backend restore'

    Write-Step 'Installing frontend dependencies with npm ci'
    Push-Location $frontendPath
    try {
        & npm ci
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
    }
    finally {
        Pop-Location
    }
    Write-Ok 'Frontend dependencies'
}

$logDirectory = Join-Path $repoRoot '.dev-logs'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$backendOut = Join-Path $logDirectory 'backend.out.log'
$backendErr = Join-Path $logDirectory 'backend.err.log'
$frontendOut = Join-Path $logDirectory 'frontend.out.log'
$frontendErr = Join-Path $logDirectory 'frontend.err.log'
Remove-Item $backendOut, $backendErr, $frontendOut, $frontendErr -Force -ErrorAction SilentlyContinue

$previousEnvironment = @{
    JwtIssuer = $env:Jwt__Issuer
    JwtAudience = $env:Jwt__Audience
    JwtSigningKey = $env:Jwt__SigningKey
    SeedData = $env:Workslip__SeedDevelopmentData
    SeedEntra = $env:Workslip__SeedDevelopmentEntraIdentities
}

$backendProcess = $null
$frontendProcess = $null
try {
    $env:Jwt__Issuer = 'workslip-local'
    $env:Jwt__Audience = 'workslip-local'
    $env:Jwt__SigningKey = New-EphemeralSigningKey
    $env:Workslip__SeedDevelopmentData = 'true'
    $env:Workslip__SeedDevelopmentEntraIdentities = 'false'

    Write-Step 'Starting backend with local-only Development configuration'
    $backendProcess = Start-Process dotnet `
        -ArgumentList @('run', '--project', $backendProject, '--launch-profile', 'http', '--no-restore') `
        -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $backendOut `
        -RedirectStandardError $backendErr `
        -PassThru

    Wait-ForUrl "$backendUrl/health" 90 $backendProcess 'Backend' $backendErr
    Write-Ok "Backend READY ($backendUrl)"

    Write-Step 'Verifying synthetic LocalJwt login and /api/auth/me'
    $tokenResponse = Invoke-RestMethod `
        -Method Post `
        -Uri "$backendUrl/api/dev/token" `
        -ContentType 'application/json' `
        -Body (@{ email = $devEmail } | ConvertTo-Json)

    if ([string]::IsNullOrWhiteSpace([string]$tokenResponse.token)) {
        throw '/api/dev/token returned no token.'
    }

    $me = Invoke-RestMethod `
        -Method Get `
        -Uri "$backendUrl/api/auth/me" `
        -Headers @{ Authorization = "Bearer $($tokenResponse.token)" }

    if ([string]::IsNullOrWhiteSpace([string]$me.email)) {
        throw '/api/auth/me returned no user email.'
    }
    Write-Ok "LocalJwt auth: $($me.email)"

    Write-Step 'Starting frontend (predev generates the branch-matched API client)'
    $frontendProcess = Start-Process npm `
        -ArgumentList @('run', 'dev') `
        -WorkingDirectory $frontendPath `
        -RedirectStandardOutput $frontendOut `
        -RedirectStandardError $frontendErr `
        -PassThru

    Wait-ForUrl $frontendUrl 120 $frontendProcess 'Frontend' $frontendErr
    Write-Ok "Frontend READY ($frontendUrl)"

    Write-Host ''
    Write-Host 'Workslip ready' -ForegroundColor Green
    Write-Host "  Backend:  $backendUrl"
    Write-Host "  Frontend: $frontendUrl"
    Write-Host "  Dev user: $devEmail"
    Write-Host "  Logs:     $logDirectory"
    Write-Host ''
    Write-Host 'Backend and frontend keep running after this script exits. Stop their process IDs when finished:'
    Write-Host "  Backend PID:  $($backendProcess.Id)"
    Write-Host "  Frontend PID: $($frontendProcess.Id)"

    if (-not $NoBrowser) {
        Start-Process $frontendUrl
    }
}
catch {
    if ($null -ne $frontendProcess -and -not $frontendProcess.HasExited) {
        Stop-Process -Id $frontendProcess.Id -Force -ErrorAction SilentlyContinue
    }
    if ($null -ne $backendProcess -and -not $backendProcess.HasExited) {
        Stop-Process -Id $backendProcess.Id -Force -ErrorAction SilentlyContinue
    }
    throw
}
finally {
    $env:Jwt__Issuer = $previousEnvironment.JwtIssuer
    $env:Jwt__Audience = $previousEnvironment.JwtAudience
    $env:Jwt__SigningKey = $previousEnvironment.JwtSigningKey
    $env:Workslip__SeedDevelopmentData = $previousEnvironment.SeedData
    $env:Workslip__SeedDevelopmentEntraIdentities = $previousEnvironment.SeedEntra
}
