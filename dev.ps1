try {
    & (Join-Path $PSScriptRoot 'tools/dev/start.ps1') @args
}
catch {
    Write-Error $_
    exit 1
}
