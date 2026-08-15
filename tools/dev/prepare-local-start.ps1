[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path

function Test-BelongsToCheckout([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
    return $Value.IndexOf($repoRoot, [StringComparison]::OrdinalIgnoreCase) -ge 0
}

foreach ($port in @(5262, 5270)) {
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue)
    foreach ($listener in $listeners) {
        $processId = [int]$listener.OwningProcess
        $processInfo = Get-CimInstance Win32_Process -Filter "ProcessId = $processId" -ErrorAction SilentlyContinue
        $commandLine = if ($null -ne $processInfo) { [string]$processInfo.CommandLine } else { '' }
        $executablePath = if ($null -ne $processInfo) { [string]$processInfo.ExecutablePath } else { '' }

        if (-not ((Test-BelongsToCheckout $commandLine) -or (Test-BelongsToCheckout $executablePath))) {
            throw "Port $port bruges af PID $processId, som ikke kan identificeres som denne Workslip-checkout. Stop processen manuelt og prøv igen."
        }

        Write-Host "[....] Stopper gammel Workslip-proces på port $port (PID $processId)" -ForegroundColor Cyan
        & taskkill.exe /PID $processId /T /F 2>$null | Out-Null
    }
}

$deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
do {
    $busy = @(5262, 5270 | Where-Object { @(Get-NetTCPConnection -State Listen -LocalPort $_ -ErrorAction SilentlyContinue).Count -gt 0 })
    if ($busy.Count -eq 0) {
        Write-Host '[OK] Lokale Workslip-porte er klar' -ForegroundColor Green
        exit 0
    }
    Start-Sleep -Milliseconds 250
} while ([DateTimeOffset]::UtcNow -lt $deadline)

throw "Kunne ikke frigive Workslip-portene: $($busy -join ', ')."
