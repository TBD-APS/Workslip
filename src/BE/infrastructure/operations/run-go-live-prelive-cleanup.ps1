param(
    [Parameter(Mandatory = $true)]
    [string]$Server,

    [Parameter(Mandatory = $true)]
    [string]$Database,

    [switch]$Execute,

    [int]$ExpectedJobCount = -1
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$cleanupScript = Join-Path $PSScriptRoot 'cleanup-prelive-orders.sql'
if (-not (Test-Path $cleanupScript)) {
    throw "Canonical WOR-348 cleanup script not found: $cleanupScript"
}

$sqlcmd = Get-Command sqlcmd -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $sqlcmd) {
    throw 'sqlcmd is required. Install the Microsoft SQL command-line tools before running go-live cleanup.'
}

if ([string]::IsNullOrWhiteSpace($Server) -or [string]::IsNullOrWhiteSpace($Database)) {
    throw 'Server and Database must both be supplied explicitly.'
}

if ($Execute -and $ExpectedJobCount -lt 0) {
    throw 'Execute requires -ExpectedJobCount with the exact JobReports count from the immediately preceding dry run.'
}

if (-not $Execute -and $ExpectedJobCount -ne -1) {
    throw 'Dry run must use the default ExpectedJobCount=-1. Run a fresh dry run immediately before execution.'
}

# Fixed first-go-live policy. These are intentionally not command-line options:
# extra cleanup decisions must be reviewed separately instead of widening WOR-348 ad hoc.
$defaultPreservedTables = @(
    'dbo.Organizations',
    'dbo.OrganizationFilials', # created by WOR-385 when that migration is deployed
    'dbo.Users',
    'dbo.Customers',
    'dbo.InstallationTypeDefinitions',
    'dbo.InstallationTypeDefinitionMappings',
    'dbo.ControlCategories',
    'dbo.ControlPoints',
    'dbo.JobWorkKinds',
    'dbo.JobClosureFlags',
    'dbo.PushSubscriptions',
    'dbo.InviteTokens',
    'dbo.WorkslipSchemaMigrations',
    'dbo.__EFMigrationsHistory'
)

$defaultJobTablesCleared = @(
    'dbo.JobReportInstallationControlPoints',
    'dbo.JobReportInstallationCategories',
    'dbo.JobReportInstallations',
    'dbo.JobReportClosureFlags',
    'dbo.JobReportLinks',
    'dbo.JobAssignments',
    'dbo.JobEvents',
    'dbo.JobViews',
    'dbo.Worksheets',
    'dbo.JobReports'
)

$defaultJobLinkedRowsClearedOnly = @(
    'dbo.NotificationDeliveryLog',
    'dbo.NotificationQueue',
    'dbo.IdempotencyRecords'
)

Write-Host ''
Write-Host 'WOR-348 go-live cleanup target' -ForegroundColor Cyan
Write-Host "  Server:   $Server"
Write-Host "  Database: $Database"
Write-Host "  Mode:     $(if ($Execute) { 'EXECUTE' } else { 'DRY RUN' })"
Write-Host ''

Write-Host 'DEFAULT KEEP - preserved completely:' -ForegroundColor Green
$defaultPreservedTables | ForEach-Object { Write-Host "  KEEP    $_" }
Write-Host ''

Write-Host 'DEFAULT CLEAR - pre-live job data:' -ForegroundColor Yellow
$defaultJobTablesCleared | ForEach-Object { Write-Host "  CLEAR   $_" }
Write-Host ''

Write-Host 'DEFAULT PARTIAL - only rows linked to deleted JobReports:' -ForegroundColor Yellow
$defaultJobLinkedRowsClearedOnly | ForEach-Object { Write-Host "  PARTIAL $_" }
Write-Host ''

if ($Execute) {
    Write-Host 'DESTRUCTIVE MODE.' -ForegroundColor Red
    Write-Host 'Prerequisites: API/background workers stopped and Azure SQL PITR/rollback verified.' -ForegroundColor Red
    Write-Host "Expected JobReports from immediately preceding dry run: $ExpectedJobCount" -ForegroundColor Red
} else {
    Write-Host 'Dry run only. No rows will be changed.' -ForegroundColor Cyan
}

$executeValue = if ($Execute) { '1' } else { '0' }

# sqlcmd supports scripting variables from environment variables. Use that path instead
# of forwarding multiple -v arguments, which isn't parsed consistently across the
# sqlcmd variants/package-manager builds used by Workslip developers on macOS/Windows.
$variableNames = @('ExpectedDatabaseName', 'ExpectedJobCount', 'Execute')
$previousEnvironmentValues = @{}
foreach ($variableName in $variableNames) {
    $previousEnvironmentValues[$variableName] = [Environment]::GetEnvironmentVariable($variableName, 'Process')
}

try {
    [Environment]::SetEnvironmentVariable('ExpectedDatabaseName', $Database, 'Process')
    [Environment]::SetEnvironmentVariable('ExpectedJobCount', [string]$ExpectedJobCount, 'Process')
    [Environment]::SetEnvironmentVariable('Execute', $executeValue, 'Process')

    $sqlcmdArguments = @(
        '-S', $Server,
        '-d', $Database,
        '-G',
        '-b',
        '-l', '30',
        '-i', $cleanupScript
    )

    & $sqlcmd.Source @sqlcmdArguments
    if ($LASTEXITCODE -ne 0) {
        throw "WOR-348 cleanup command failed with sqlcmd exit code $LASTEXITCODE. No success claim should be made; review the SQL error and rollback state."
    }
}
finally {
    foreach ($variableName in $variableNames) {
        [Environment]::SetEnvironmentVariable(
            $variableName,
            $previousEnvironmentValues[$variableName],
            'Process')
    }
}

if ($Execute) {
    Write-Host ''
    Write-Host 'Cleanup command completed. Review the SQL post-check output before reopening production.' -ForegroundColor Green
} else {
    Write-Host ''
    Write-Host 'Dry run completed. Use the reported JobReports count as -ExpectedJobCount only for the immediately following approved execution.' -ForegroundColor Cyan
}
