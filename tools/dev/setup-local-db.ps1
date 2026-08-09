param(
    [string]$DatabaseName = 'WorkslipLocal'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($env:OS -ne 'Windows_NT') {
    throw 'Workslip local DB bootstrap currently supports Windows SQL Server LocalDB only.'
}

if ($DatabaseName -notmatch '^[A-Za-z0-9_-]+$') {
    throw "Invalid database name '$DatabaseName'. Use letters, numbers, hyphens or underscores only."
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnet) {
    throw '.NET SDK is required. Install the Workslip-supported .NET SDK before running local DB setup.'
}

$localDb = Get-Command sqllocaldb.exe -ErrorAction SilentlyContinue
if ($null -eq $localDb) {
    throw 'SQL Server LocalDB is required. Install Microsoft SQL Server Express LocalDB, then rerun this command.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$apiPath = Join-Path $repoRoot 'src\BE\WorkslipApi'
$settingsPath = Join-Path $apiPath 'appsettings.Development.json'
$instanceName = 'MSSQLLocalDB'

& $localDb.Source info $instanceName *> $null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Creating SQL Server LocalDB instance '$instanceName'..."
    & $localDb.Source create $instanceName | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Could not create SQL Server LocalDB instance '$instanceName'."
    }
}

& $localDb.Source start $instanceName | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Could not start SQL Server LocalDB instance '$instanceName'."
}

$connectionString = "Server=(localdb)\$instanceName;Database=$DatabaseName;Integrated Security=true;TrustServerCertificate=true"

Write-Host "Bootstrapping local Workslip database '$DatabaseName'..."
Push-Location $apiPath
try {
    $dotnetArguments = @(
        'run',
        '--configuration', 'Release',
        '--no-launch-profile',
        '--',
        '--environment', 'Development',
        '--Azure:AppConfiguration:Endpoint=',
        "--Azure:Sql:ConnectionString=$connectionString",
        '--Workslip:Operation=bootstrap-local-db'
    )

    & $dotnet.Source @dotnetArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Workslip local database bootstrap failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

function Get-OrAddObjectProperty {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        $value = [pscustomobject]@{}
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $value -Force
        return $value
    }

    if ($property.Value -isnot [pscustomobject]) {
        throw "Cannot update '$settingsPath': '$Name' must be a JSON object."
    }

    return $property.Value
}

if (Test-Path $settingsPath) {
    $rawSettings = Get-Content -Path $settingsPath -Raw
    if ([string]::IsNullOrWhiteSpace($rawSettings)) {
        $settings = [pscustomobject]@{}
    }
    else {
        $settings = $rawSettings | ConvertFrom-Json
    }
}
else {
    $settings = [pscustomobject]@{}
}

$azure = Get-OrAddObjectProperty -Object $settings -Name 'Azure'
$sql = Get-OrAddObjectProperty -Object $azure -Name 'Sql'
$sql | Add-Member -NotePropertyName 'ConnectionString' -NotePropertyValue $connectionString -Force

$workslip = Get-OrAddObjectProperty -Object $settings -Name 'Workslip'
$workslip | Add-Member -NotePropertyName 'ApplyLocalMigrations' -NotePropertyValue $true -Force

$json = $settings | ConvertTo-Json -Depth 20
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($settingsPath, $json + [Environment]::NewLine, $utf8NoBom)

Write-Host ''
Write-Host 'Local Workslip database is ready.' -ForegroundColor Green
Write-Host "  Database: $DatabaseName"
Write-Host "  Config:   $settingsPath"
Write-Host 'Future branch migrations will be applied automatically on normal Development startup.'
