[CmdletBinding()]
param(
    [ValidateSet('up', 'start', 'demo', 'status', 'ps', 'logs', 'log', 'down', 'stop')]
    [string]$Command = 'up'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($env:OS -ne 'Windows_NT') {
    throw 'scripts/demo.ps1 is the native Windows wrapper. Use scripts/demo.sh on macOS/Linux.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$frontendUrl = if ($env:WORKSLIP_LOCAL_URL) { $env:WORKSLIP_LOCAL_URL } else { 'http://127.0.0.1:5270' }
$apiUrl = if ($env:WORKSLIP_API_URL) { $env:WORKSLIP_API_URL } else { 'http://127.0.0.1:5262' }
$seqUrl = if ($env:WORKSLIP_SEQ_URL) { $env:WORKSLIP_SEQ_URL } else { 'http://127.0.0.1:5341' }

function Write-Step([string]$Message) {
    Write-Host "[workslip demo] $Message" -ForegroundColor Cyan
}

function Test-Url([string]$Url) {
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3
        return $response.StatusCode -ge 200 -and $response.StatusCode -lt 400
    }
    catch {
        return $false
    }
}

function Wait-ForUrl([string]$Name, [string]$Url, [int]$TimeoutSeconds) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (Test-Url $Url) { return }
        Start-Sleep -Seconds 2
    }

    Write-Host ''
    & docker compose ps
    & docker compose logs --tail 120 api fe
    throw "$Name did not become reachable at $Url within $TimeoutSeconds seconds."
}

function Test-DockerDaemon {
    try {
        & docker info *> $null
        return $LASTEXITCODE -eq 0
    }
    catch {
        return $false
    }
}

function Start-DockerDesktopIfAvailable {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Docker\Docker\Docker Desktop.exe'),
        (Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe')
    ) | Where-Object { $_ -and (Test-Path $_) }

    $desktop = $candidates | Select-Object -First 1
    if (-not $desktop) { return $false }

    Write-Step 'Docker daemon is not responding; starting Docker Desktop'
    Start-Process -FilePath $desktop | Out-Null
    return $true
}

function Ensure-Docker {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'Docker CLI is required. Install Docker Desktop with the WSL 2 backend, or another Docker-compatible engine with Compose v2.'
    }

    if (-not (Test-DockerDaemon)) {
        if (-not (Start-DockerDesktopIfAvailable)) {
            throw 'Docker is installed but the daemon is not reachable. Start your Docker engine and retry.'
        }

        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(120)
        while ([DateTimeOffset]::UtcNow -lt $deadline -and -not (Test-DockerDaemon)) {
            Start-Sleep -Seconds 2
        }
    }

    if (-not (Test-DockerDaemon)) {
        throw 'Docker Desktop started, but the Docker daemon did not become reachable.'
    }

    & docker compose version *> $null
    if ($LASTEXITCODE -ne 0) { throw 'Docker Compose v2 is required.' }
}

function Invoke-Up {
    Ensure-Docker
    Push-Location $repoRoot
    try {
        Write-Step 'Validating Compose configuration'
        & docker compose config --quiet
        if ($LASTEXITCODE -ne 0) { throw 'docker compose config validation failed.' }

        Write-Step 'Starting Workslip full local stack'
        & docker compose up -d --wait --quiet-pull --progress plain
        if ($LASTEXITCODE -ne 0) { throw 'docker compose up failed.' }

        Write-Step 'Waiting for API health'
        Wait-ForUrl 'Workslip API' "$apiUrl/health" 240
        Write-Step 'Waiting for frontend'
        Wait-ForUrl 'Workslip frontend' $frontendUrl 240

        Write-Host ''
        Write-Host "WORKSLIP_URL=$frontendUrl"
        Write-Host "WORKSLIP_API_URL=$apiUrl"
        Write-Host "WORKSLIP_SEQ_URL=$seqUrl"
        Write-Host ''
    }
    finally { Pop-Location }
}

function Invoke-Status {
    Ensure-Docker
    Push-Location $repoRoot
    try {
        & docker compose ps
        Write-Host ''
        if (Test-Url $frontendUrl) { Write-Host "[workslip demo] Frontend: healthy ($frontendUrl)" }
        else { Write-Host "[workslip demo] Frontend: unavailable ($frontendUrl)" }
        if (Test-Url "$apiUrl/health") { Write-Host "[workslip demo] API: healthy ($apiUrl/health)" }
        else { Write-Host "[workslip demo] API: unavailable ($apiUrl/health)" }
    }
    finally { Pop-Location }
}

function Invoke-Logs {
    Ensure-Docker
    Push-Location $repoRoot
    try { & docker compose logs --tail 200 -f api fe db seq }
    finally { Pop-Location }
}

function Invoke-Down {
    Ensure-Docker
    Push-Location $repoRoot
    try {
        Write-Step 'Stopping Workslip (persistent volumes are preserved)'
        & docker compose down
    }
    finally { Pop-Location }
}

switch ($Command) {
    { $_ -in @('up', 'start', 'demo') } { Invoke-Up; break }
    { $_ -in @('status', 'ps') } { Invoke-Status; break }
    { $_ -in @('logs', 'log') } { Invoke-Logs; break }
    { $_ -in @('down', 'stop') } { Invoke-Down; break }
}
