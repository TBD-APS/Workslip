[CmdletBinding()]
param(
    [switch]$NoOpen
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

function Fail([string]$Message) {
    Write-Host ""
    Write-Host "[ERROR] $Message" -ForegroundColor Red
    exit 1
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Host "Docker CLI blev ikke fundet på denne computer." -ForegroundColor Yellow
    Write-Host "Installer/start Docker Desktop og åbn derefter en ny VS Code-terminal." -ForegroundColor Yellow
    Write-Host "Windows-installation: winget install -e --id Docker.DockerDesktop" -ForegroundColor Cyan
    Fail "Docker er ikke tilgængelig i PATH."
}

try {
    docker version *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "Docker Engine svarer ikke."
    }
}
catch {
    Write-Host "Docker CLI findes, men Docker Engine kører ikke." -ForegroundColor Yellow
    Write-Host "Start Docker Desktop. Hvis WSL2/virtualisering fejler, skal virtualisering være aktiveret i BIOS/UEFI og Windows Virtual Machine Platform være slået til." -ForegroundColor Yellow
    Fail $_.Exception.Message
}

Write-Host "[1/4] Validerer Docker Compose..." -ForegroundColor Cyan
docker compose config --quiet
if ($LASTEXITCODE -ne 0) { Fail "docker-compose.yml kunne ikke valideres." }

Write-Host "[2/4] Starter Workslip SQL, Seq, API og frontend..." -ForegroundColor Cyan
docker compose up -d
if ($LASTEXITCODE -ne 0) { Fail "Docker Compose kunne ikke starte Workslip." }

Write-Host "[3/4] Containerstatus:" -ForegroundColor Cyan
docker compose ps

Write-Host "[4/4] Workslip starter. Første opstart kan tage nogle minutter pga. image pull, npm ci og NuGet restore." -ForegroundColor Cyan
Write-Host ""
Write-Host "Workslip : http://127.0.0.1:5270" -ForegroundColor Green
Write-Host "Overblik : http://127.0.0.1:5270/app/overblik" -ForegroundColor Green
Write-Host "API      : http://127.0.0.1:5262" -ForegroundColor Green
Write-Host "Seq      : http://127.0.0.1:5341" -ForegroundColor Green
Write-Host ""
Write-Host "Logs: docker compose logs -f --tail=150" -ForegroundColor DarkGray

if (-not $NoOpen) {
    Start-Process 'http://127.0.0.1:5270/app/overblik'
}
