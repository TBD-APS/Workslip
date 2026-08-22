[CmdletBinding()]
param(
    [switch]$SkipInstall,
    [switch]$NoBrowser,
    [switch]$CheckOnly,
    [switch]$Mobile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$frontendUrl = 'http://127.0.0.1:5270'
$backendUrl = 'http://127.0.0.1:5262'
$overviewPath = '/app/overblik'

function Write-Ok([string]$Message) {
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Write-Step([string]$Message) {
    Write-Host "[....] $Message" -ForegroundColor Cyan
}

function Test-Url([string]$Url) {
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3
        return $response.StatusCode -ge 200 -and $response.StatusCode -lt 500
    }
    catch {
        return $false
    }
}

function Wait-ForUrl([string]$Url, [int]$TimeoutSeconds, [string]$Name) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (Test-Url $Url) {
            return
        }
        Start-Sleep -Milliseconds 750
    }

    Write-Host ''
    Write-Host "Last Docker logs for ${Name}:" -ForegroundColor Yellow
    & docker compose logs --tail 80
    throw "$Name did not become ready at $Url within $TimeoutSeconds seconds."
}

function Get-LanIPv4 {
    if ($IsMacOS) {
        $routeOutput = @(& route -n get default 2>$null)
        $interfaceLine = $routeOutput | Where-Object { $_ -match '^\s*interface:\s*(\S+)' } | Select-Object -First 1
        if ($interfaceLine) {
            $match = [regex]::Match([string]$interfaceLine, '^\s*interface:\s*(\S+)')
            if ($match.Success) {
                $interface = $match.Groups[1].Value
                $candidate = [string](& ipconfig getifaddr $interface 2>$null)
                if (-not [string]::IsNullOrWhiteSpace($candidate)) {
                    return $candidate.Trim()
                }
            }
        }
    }

    if ($IsLinux) {
        $candidates = @([string](& hostname -I 2>$null) -split '\s+' | Where-Object {
            $_ -match '^\d+\.\d+\.\d+\.\d+$' -and $_ -notmatch '^127\.' -and $_ -notmatch '^169\.254\.'
        })
        if ($candidates.Count -gt 0) {
            return $candidates[0]
        }
    }

    return $null
}

if ($env:OS -eq 'Windows_NT') {
    throw 'tools/dev/start-docker.ps1 is intended for macOS/Linux. Windows uses tools/dev/start.ps1.'
}

Write-Host 'Workslip Docker Dev Doctor' -ForegroundColor White
Write-Host '--------------------------' -ForegroundColor DarkGray

$docker = Get-Command docker -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $docker) {
    throw 'Docker is required. Install/start Docker Desktop and retry.'
}

& $docker.Source version *> $null
if ($LASTEXITCODE -ne 0) {
    throw 'Docker is installed but the daemon is not reachable. Start Docker Desktop and retry.'
}

& $docker.Source compose version *> $null
if ($LASTEXITCODE -ne 0) {
    throw 'Docker Compose v2 is required.'
}
Write-Ok 'Docker + Compose available'

Push-Location $repoRoot
try {
    & $docker.Source compose config --quiet
    if ($LASTEXITCODE -ne 0) {
        throw 'docker compose config validation failed.'
    }
    Write-Ok 'docker-compose.yml is valid'

    $git = Get-Command git -ErrorAction SilentlyContinue | Select-Object -First 1
    $branch = '<unknown>'
    $sha = '<unknown>'
    if ($null -ne $git) {
        $branchValue = [string](& $git.Source -C $repoRoot branch --show-current)
        $shaValue = [string](& $git.Source -C $repoRoot rev-parse --short HEAD)
        if (-not [string]::IsNullOrWhiteSpace($branchValue)) { $branch = $branchValue.Trim() }
        if (-not [string]::IsNullOrWhiteSpace($shaValue)) { $sha = $shaValue.Trim() }
    }

    $lanIp = $null
    if ($Mobile) {
        $lanIp = Get-LanIPv4
        if ([string]::IsNullOrWhiteSpace([string]$lanIp)) {
            throw 'Mobile mode could not resolve this Mac/Linux machine LAN IPv4 address. Connect the computer and phone to the same trusted Wi-Fi/LAN.'
        }
        Write-Ok "Phone LAN address: $lanIp"
    }

    if ($CheckOnly) {
        Write-Host ''
        Write-Host "Doctor checks passed for branch $branch ($sha). No containers were changed." -ForegroundColor Green
        exit 0
    }

    Write-Step "Starting Docker full stack from branch $branch ($sha)"
    & $docker.Source compose up -d --remove-orphans
    if ($LASTEXITCODE -ne 0) {
        throw 'docker compose up failed.'
    }

    Wait-ForUrl "$backendUrl/health" 150 'Backend'
    Write-Ok "Backend READY ($backendUrl)"

    Wait-ForUrl $frontendUrl 180 'Frontend'
    Write-Ok "Frontend READY ($frontendUrl)"

    $desktopOverview = "$frontendUrl$overviewPath"
    Write-Host ''
    Write-Host 'Workslip ready' -ForegroundColor Green
    Write-Host "  Branch:   $branch"
    Write-Host "  Commit:   $sha"
    Write-Host "  Backend:  $backendUrl"
    Write-Host "  Frontend: $frontendUrl"
    Write-Host "  Overblik: $desktopOverview"

    if ($Mobile) {
        $phoneUrl = "http://${lanIp}:5270$overviewPath"
        if (-not (Test-Url "http://${lanIp}:5270")) {
            Write-Host '  Warning: frontend is healthy on localhost, but the LAN URL was not reachable from this machine.' -ForegroundColor Yellow
            Write-Host '  Check macOS firewall/VPN settings and ensure Docker Desktop allows the published port.' -ForegroundColor Yellow
        }
        Write-Host "  Phone:    $phoneUrl" -ForegroundColor Cyan
        Write-Host '  Phone and Mac must be on the same trusted Wi-Fi/LAN.'
        Write-Host '  API calls stay same-origin and are proxied by Vite to the API container.'
    }

    Write-Host ''
    Write-Host 'Containers keep running after this script exits. Stop them with:'
    Write-Host '  docker compose down'

    if (-not $NoBrowser) {
        if ($IsMacOS) {
            & open $desktopOverview
        }
        elseif ($IsLinux) {
            $xdgOpen = Get-Command xdg-open -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($null -ne $xdgOpen) {
                & $xdgOpen.Source $desktopOverview *> $null
            }
        }
    }
}
finally {
    Pop-Location
}
