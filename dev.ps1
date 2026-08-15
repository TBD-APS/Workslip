$ErrorActionPreference = 'Stop'

try {
    & (Join-Path $PSScriptRoot 'tools/dev/start.ps1') @args
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
