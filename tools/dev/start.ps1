[CmdletBinding()]
param(
    [switch]$SkipInstall,
    [switch]$NoBrowser,
    [switch]$CheckOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$backendPath = Join-Path $repoRoot 'src/BE/WorkslipApi'
$backendProject = Join-Path $backendPath 'Workslip.Api.csproj'
$frontendPath = Join-Path $repoRoot 'src/FE'
$viteEntry = Join-Path $frontendPath 'node_modules/vite/bin/vite.js'
$backendUrl = 'http://localhost:5262'
$frontendUrl = 'http://127.0.0.1:5270'
$overviewUrl = "$frontendUrl/app/overblik"
$devEmail = 'admin@17v3ygzs.mailosaur.net'

function Write-Ok([string]$Message) {
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Write-Step([string]$Message) {
    Write-Host "[....] $Message" -ForegroundColor Cyan
}

function Get-RequiredCommand([string]$Name, [string]$InstallHint) {
    $command = Get-Command $Name -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $command) {
        throw "$Name is required. $InstallHint"
    }
    return $command.Source
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

function Get-LogTail([string[]]$LogPaths) {
    $sections = @()
    foreach ($path in $LogPaths) {
        if (Test-Path $path) {
            $sections += "--- $path ---"
            $sections += (Get-Content $path -Tail 40)
        }
    }
    if ($sections.Count -eq 0) {
        return '<no logs>'
    }
    return $sections -join [Environment]::NewLine
}

function Wait-ForUrl(
    [string]$Url,
    [int]$TimeoutSeconds,
    [System.Diagnostics.Process]$Process,
    [string]$Name,
    [string[]]$LogPaths) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (Test-Url $Url) {
            return
        }

        if ($Process.HasExited) {
            throw "$Name exited before becoming ready.`n$(Get-LogTail $LogPaths)"
        }

        Start-Sleep -Milliseconds 500
    }

    throw "$Name did not become ready at $Url within $TimeoutSeconds seconds.`n$(Get-LogTail $LogPaths)"
}

function Stop-StaleWorkslipListener([int]$Port, [string]$ServiceName) {
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
    if ($listeners.Count -eq 0) {
        return
    }

    foreach ($listener in $listeners) {
        $processId = [int]$listener.OwningProcess
        $processInfo = Get-CimInstance Win32_Process -Filter "ProcessId = $processId" -ErrorAction SilentlyContinue
        $commandLine = [string]$processInfo.CommandLine
        $executablePath = [string]$processInfo.ExecutablePath
        $isWorkslipProcess =
            $commandLine.Contains($repoRoot, [StringComparison]::OrdinalIgnoreCase) -or
            $executablePath.Contains($repoRoot, [StringComparison]::OrdinalIgnoreCase)

        if (-not $isWorkslipProcess) {
            throw "Port $Port is already in use by PID $processId ($ServiceName), and it is not identifiable as this Workslip checkout. Stop that process manually before running dev.ps1."
        }

        Write-Step "Stopping stale Workslip $ServiceName process on port $Port (PID $processId)"
        & taskkill.exe /PID $processId /T /F 2>$null | Out-Null
    }

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (@(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue).Count -eq 0) {
            Write-Ok "Port $Port is free"
            return
        }
        Start-Sleep -Milliseconds 250
    }

    throw "Could not free port $Port after stopping the stale Workslip $ServiceName process."
}

function Assert-PortFree([int]$Port, [string]$ServiceName) {
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
    if ($listeners.Count -gt 0) {
        throw "Port $Port is already in use. Stop the existing $ServiceName/local process before running dev.ps1."
    }
}

function New-EphemeralSigningKey {
    $bytes = New-Object byte[] 48
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }
    return [Convert]::ToBase64String($bytes)
}

function Stop-ProcessTree([System.Diagnostics.Process]$Process) {
    if ($null -eq $Process -or $Process.HasExited) {
        return
    }
    & taskkill.exe /PID $Process.Id /T /F 2>$null | Out-Null
}

if ($env:OS -ne 'Windows_NT') {
    throw 'The canonical Workslip bootstrap currently supports Windows only.'
}

Write-Host 'Workslip Local Dev Doctor' -ForegroundColor White
Write-Host '-------------------------' -ForegroundColor DarkGray

$dotnetCommand = Get-RequiredCommand 'dotnet' 'Install the .NET 10 SDK.'
$nodeCommand = Get-RequiredCommand 'node' 'Install Node.js 24.'
$npmResolved = Get-Command 'npm.cmd' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $npmResolved) {
    throw 'npm.cmd is required. Install Node.js 24 for Windows.'
}
$npmCommand = $npmResolved.Source
$localDbCommand = Get-RequiredCommand 'sqllocaldb' 'Install SQL Server LocalDB.'

$dotnetVersion = (& $dotnetCommand --version)
$nodeVersion = (& $nodeCommand --version)
$npmVersion = (& $npmCommand --version)
Assert-MajorVersion '.NET SDK' $dotnetVersion 10
Assert-MajorVersion 'Node.js' $nodeVersion 24
Write-Ok ".NET SDK $dotnetVersion"
Write-Ok "Node.js $nodeVersion / npm $npmVersion"

$localDbInstances = @(& $localDbCommand info 2>$null | ForEach-Object { $_.Trim() })
if ($localDbInstances -notcontains 'MSSQLLocalDB') {
    if ($CheckOnly) {
        throw 'MSSQLLocalDB is not created yet. Run .\dev.ps1 once to create the local-only instance.'
    }
    Write-Step 'Creating local MSSQLLocalDB instance'
    & $localDbCommand create MSSQLLocalDB | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create MSSQLLocalDB.' }
}

if (-not $CheckOnly) {
    & $localDbCommand start MSSQLLocalDB | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not start MSSQLLocalDB.' }
}
Write-Ok 'SQL Server LocalDB MSSQLLocalDB'

if (-not (Test-Path $backendProject)) { throw "Backend project not found: $backendProject" }
if (-not (Test-Path (Join-Path $frontendPath 'package-lock.json'))) { throw 'Frontend package-lock.json is missing.' }
if (-not (Test-Path (Join-Path $backendPath 'appsettings.Development.json'))) { throw 'Tracked appsettings.Development.json is missing.' }
Write-Ok 'Tracked Development configuration present'

if ($CheckOnly) {
    Assert-PortFree 5262 'backend'
    Assert-PortFree 5270 'frontend'

    Write-Host ''
    Write-Host 'Doctor checks passed. No dependencies, databases or processes were changed.' -ForegroundColor Green
    exit 0
}

Stop-StaleWorkslipListener 5262 'backend'
Stop-StaleWorkslipListener 5270 'frontend'

if (-not $SkipInstall) {
    Write-Step 'Restoring backend dependencies'
    & $dotnetCommand restore $backendProject --nologo
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
    Write-Ok 'Backend restore'

    Write-Step 'Installing frontend dependencies with npm ci'
    Push-Location $frontendPath
    try {
        & $npmCommand ci
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
    }
    finally {
        Pop-Location
    }
    Write-Ok 'Frontend dependencies'
}

if (-not (Test-Path $viteEntry)) {
    throw 'Vite is not installed. Run .\dev.ps1 without -SkipInstall.'
}

Write-Step 'Generating branch-matched frontend API client before backend startup'
Push-Location $frontendPath
try {
    & $npmCommand run generate:api:local -- --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Frontend API client generation failed.' }
    & $npmCommand run sync:fonts
    if ($LASTEXITCODE -ne 0) { throw 'Frontend font sync failed.' }
}
finally {
    Pop-Location
}
Write-Ok 'Frontend generated assets'

$logDirectory = Join-Path ([IO.Path]::GetTempPath()) 'workslip-dev-logs'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$backendOut = Join-Path $logDirectory 'backend.out.log'
$backendErr = Join-Path $logDirectory 'backend.err.log'
$frontendOut = Join-Path $logDirectory 'frontend.out.log'
$frontendErr = Join-Path $logDirectory 'frontend.err.log'
Remove-Item $backendOut, $backendErr, $frontendOut, $frontendErr -Force -ErrorAction SilentlyContinue

$previousJwtIssuer = $env:Jwt__Issuer
$previousJwtAudience = $env:Jwt__Audience
$previousJwtSigningKey = $env:Jwt__SigningKey
$previousSeedData = $env:Workslip__SeedDevelopmentData
$previousSeedEntra = $env:Workslip__SeedDevelopmentEntraIdentities

$backendProcess = $null
$frontendProcess = $null
try {
    $env:Jwt__Issuer = 'workslip-local'
    $env:Jwt__Audience = 'workslip-local'
    $env:Jwt__SigningKey = New-EphemeralSigningKey
    $env:Workslip__SeedDevelopmentData = 'true'
    $env:Workslip__SeedDevelopmentEntraIdentities = 'false'

    Write-Step 'Starting backend with Development-only local auth and synthetic data'
    $backendProcess = Start-Process $dotnetCommand `
        -ArgumentList @('run', '--project', $backendProject, '--launch-profile', 'http', '--no-restore') `
        -WorkingDirectory $repoRoot `
        -RedirectStandardOutput $backendOut `
        -RedirectStandardError $backendErr `
        -PassThru

    Wait-ForUrl "$backendUrl/health" 90 $backendProcess 'Backend' @($backendOut, $backendErr)
    Write-Ok "Backend READY ($backendUrl)"

    Write-Step 'Verifying /api/dev/token -> /api/auth/me'
    $tokenResponse = Invoke-RestMethod `
        -Method Post `
        -Uri "$backendUrl/api/dev/token" `
        -ContentType 'application/json' `
        -Body (@{ email = $devEmail } | ConvertTo-Json)

    if ([string]::IsNullOrWhiteSpace([string]$tokenResponse.token)) {
        throw '/api/dev/token returned no token.'
    }

    $authHeaders = @{ Authorization = "Bearer $($tokenResponse.token)" }
    $me = Invoke-RestMethod `
        -Method Get `
        -Uri "$backendUrl/api/auth/me" `
        -Headers $authHeaders

    if ([string]::IsNullOrWhiteSpace([string]$me.email)) {
        throw '/api/auth/me returned no user email.'
    }
    Write-Ok "LocalJwt auth: $($me.email)"

    Write-Step 'Verifying /api/jobs/overview with the local dev user'
    $overview = Invoke-RestMethod `
        -Method Get `
        -Uri "$backendUrl/api/jobs/overview" `
        -Headers $authHeaders

    foreach ($propertyName in @('activeCount', 'inReviewCount', 'approvedCount', 'rejectedCount', 'recentJobs')) {
        if ($null -eq $overview.PSObject.Properties[$propertyName]) {
            throw "/api/jobs/overview response is missing '$propertyName'."
        }
    }
    Write-Ok "Overview API READY (active=$($overview.activeCount), review=$($overview.inReviewCount), approved=$($overview.approvedCount), rejected=$($overview.rejectedCount))"

    Write-Step 'Starting frontend using the already generated local contract'
    $viteArgument = '"' + $viteEntry + '"'
    $frontendProcess = Start-Process $nodeCommand `
        -ArgumentList $viteArgument `
        -WorkingDirectory $frontendPath `
        -RedirectStandardOutput $frontendOut `
        -RedirectStandardError $frontendErr `
        -PassThru

    Wait-ForUrl $frontendUrl 120 $frontendProcess 'Frontend' @($frontendOut, $frontendErr)
    Write-Ok "Frontend READY ($frontendUrl)"

    Write-Host ''
    Write-Host 'Workslip ready' -ForegroundColor Green
    Write-Host "  Backend:  $backendUrl"
    Write-Host "  Frontend: $frontendUrl"
    Write-Host "  Overblik: $overviewUrl"
    Write-Host "  Dev user: $devEmail"
    Write-Host "  Logs:     $logDirectory"
    Write-Host ''
    Write-Host 'Backend and frontend keep running after this script exits. Stop them with:'
    Write-Host "  taskkill /PID $($backendProcess.Id) /T /F"
    Write-Host "  taskkill /PID $($frontendProcess.Id) /T /F"

    if (-not $NoBrowser) {
        Start-Process $overviewUrl
    }
}
catch {
    Stop-ProcessTree $frontendProcess
    Stop-ProcessTree $backendProcess
    throw
}
finally {
    $env:Jwt__Issuer = $previousJwtIssuer
    $env:Jwt__Audience = $previousJwtAudience
    $env:Jwt__SigningKey = $previousJwtSigningKey
    $env:Workslip__SeedDevelopmentData = $previousSeedData
    $env:Workslip__SeedDevelopmentEntraIdentities = $previousSeedEntra
}
