[CmdletBinding()]
param(
    [switch]$Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($env:OS -ne 'Windows_NT') {
    throw 'This installer is for Windows. Use `make install-global` on macOS/Linux.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$targetDir = Join-Path $env:LOCALAPPDATA 'Workslip\bin'
$targetCmd = Join-Path $targetDir 'workslip.cmd'

function Get-UserPathParts {
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if ([string]::IsNullOrWhiteSpace($userPath)) { return @() }
    return @($userPath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

if ($Uninstall) {
    if (Test-Path $targetCmd) { Remove-Item $targetCmd -Force }
    $parts = @(Get-UserPathParts | Where-Object { $_.TrimEnd('\\') -ne $targetDir.TrimEnd('\\') })
    [Environment]::SetEnvironmentVariable('Path', ($parts -join ';'), 'User')
    Write-Host "Removed global Workslip command: $targetCmd"
    Write-Host 'Open a new terminal to pick up the PATH change.'
    exit 0
}

New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
$demoScript = Join-Path $repoRoot 'scripts\demo.ps1'

$wrapper = @"
@echo off
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "$demoScript" %*
"@
Set-Content -Path $targetCmd -Value $wrapper -Encoding Ascii

$parts = @(Get-UserPathParts)
if (-not ($parts | Where-Object { $_.TrimEnd('\\') -eq $targetDir.TrimEnd('\\') })) {
    $newUserPath = (@($parts) + $targetDir) -join ';'
    [Environment]::SetEnvironmentVariable('Path', $newUserPath, 'User')
}

Write-Host "Installed global Workslip command: $targetCmd" -ForegroundColor Green
Write-Host "Repository: $repoRoot"
Write-Host ''
Write-Host 'Open a new PowerShell/Terminal window, then run:'
Write-Host '  workslip'
Write-Host '  workslip status'
Write-Host '  workslip logs'
Write-Host '  workslip down'
Write-Host ''
Write-Host 'If the repository is moved, run this installer again from the new checkout.'
