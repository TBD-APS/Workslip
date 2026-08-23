<#
.SYNOPSIS
    Seed or perform the final Workslip Azure SQL copy into the new tenant.

.DESCRIPTION
    Copies db-mrsoftware-prod to the new tenant with Azure SQL's transactionally
    consistent CREATE DATABASE ... AS COPY OF flow. Cross-tenant database copy
    requires SQL authentication, so the script creates one short-lived login with
    the same password/SID on both logical servers, removes it afterward, and never
    writes the generated credential to disk.

    Seed mode leaves current production online and is intended to populate the new
    tenant for Entra/API verification. Final mode freezes the current API before the
    snapshot and leaves it stopped after success so no post-copy writes can diverge.

    The script writes only non-personal migration evidence (table names/counts and
    operation metadata) to a local JSON manifest.

.EXAMPLE
    ./copy-new-tenant-production-data.ps1 `
      -Mode Seed `
      -SourceTenantId <guid> -SourceSubscriptionId <guid> `
      -TargetTenantId <guid> -TargetSubscriptionId <guid> `
      -Confirmation 'SEED NEW TENANT DATABASE'

.EXAMPLE
    ./copy-new-tenant-production-data.ps1 `
      -Mode Final `
      -SourceTenantId <guid> -SourceSubscriptionId <guid> `
      -TargetTenantId <guid> -TargetSubscriptionId <guid> `
      -Confirmation 'FINAL COPY NEW TENANT DATABASE'
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Seed', 'Final')]
    [string]$Mode,

    [Parameter(Mandatory = $true)]
    [string]$SourceTenantId,

    [Parameter(Mandatory = $true)]
    [string]$SourceSubscriptionId,

    [Parameter(Mandatory = $true)]
    [string]$TargetTenantId,

    [Parameter(Mandatory = $true)]
    [string]$TargetSubscriptionId,

    [Parameter(Mandatory = $true)]
    [string]$Confirmation,

    [string]$ManifestPath = '',
    [string]$ClientIp = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$source = [ordered]@{
    TenantId       = $SourceTenantId
    SubscriptionId = $SourceSubscriptionId
    ResourceGroup  = 'rg-mrsoftware-prod'
    SqlServer      = 'db-mrsoftware-prod-server'
    Database       = 'db-mrsoftware-prod'
    KeyVault       = 'kv-mrsoftware-prod'
    WebApp         = 'api-mrsoftware-prod'
}

$target = [ordered]@{
    TenantId       = $TargetTenantId
    SubscriptionId = $TargetSubscriptionId
    ResourceGroup  = 'rg-mrsoftwarev2-live'
    SqlServer      = 'db-mrsoftwarev2-live-server'
    Database       = 'db-mrsoftwarev2-live'
    KeyVault       = 'kv-mrsoftwarev2-live'
    WebApp         = 'api-mrsoftwarev2-live'
}

$sqlAdminLogin = 'rbj'
$sqlAdminPasswordSecretName = 'Azure--Sql--AdminPassword'
$timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
$tempLogin = "workslip_cutover_$timestamp"
$copyDatabase = "$($target.Database)-copy-$timestamp"
$quarantineDatabase = "$($target.Database)-quarantine-$timestamp"
$firewallRuleName = "AllowWorkslipCutover-$timestamp"
$sourceWasRunning = $false
$targetWasRunning = $false
$targetWasStoppedByScript = $false
$copySucceeded = $false
$swapSucceeded = $false
$operationSucceeded = $false
$sourceAdminPassword = $null
$targetAdminPassword = $null
$tempPassword = $null

function Assert-GuidValue {
    param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)][string]$Value)
    $parsed = [Guid]::Empty
    if (-not [Guid]::TryParse($Value, [ref]$parsed)) {
        throw "$Name must be a GUID. Received '$Value'."
    }
}

function Assert-SqlIdentifier {
    param([Parameter(Mandatory = $true)][string]$Value)
    if ($Value -notmatch '^[A-Za-z0-9_-]+$') {
        throw "Unsafe SQL identifier '$Value'."
    }
}

function ConvertTo-SqlIdentifier {
    param([Parameter(Mandatory = $true)][string]$Value)
    Assert-SqlIdentifier -Value $Value
    return "[$Value]"
}

function ConvertTo-SqlStringLiteral {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)
    $escaped = $Value.Replace("'", "''")
    return "N'$escaped'"
}

function Invoke-AzureCli {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    $output = @(& az @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    $text = ($output -join [Environment]::NewLine).Trim()

    if ($exitCode -ne 0 -and -not $AllowFailure) {
        if ([string]::IsNullOrWhiteSpace($text)) {
            $text = 'Azure CLI returned no diagnostic output.'
        }
        throw "Azure CLI failed with exit code $exitCode.`n$text"
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output = $text
    }
}

function Set-AzureContext {
    param(
        [Parameter(Mandatory = $true)][string]$TenantId,
        [Parameter(Mandatory = $true)][string]$SubscriptionId,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $setResult = Invoke-AzureCli -Arguments @('account', 'set', '--subscription', $SubscriptionId) -AllowFailure
    if ($setResult.ExitCode -ne 0) {
        Write-Host "$Label Azure session is not cached. Opening Azure login for tenant $TenantId ..."
        Invoke-AzureCli -Arguments @('login', '--tenant', $TenantId, '--output', 'none') | Out-Null
        Invoke-AzureCli -Arguments @('account', 'set', '--subscription', $SubscriptionId) | Out-Null
    }

    $accountJson = (Invoke-AzureCli -Arguments @(
        'account', 'show',
        '--query', '{tenantId:tenantId,id:id,name:name}',
        '--output', 'json'
    )).Output | ConvertFrom-Json

    if ($accountJson.tenantId -ne $TenantId) {
        throw "$Label tenant mismatch. Expected $TenantId, Azure CLI selected $($accountJson.tenantId)."
    }
    if ($accountJson.id -ne $SubscriptionId) {
        throw "$Label subscription mismatch. Expected $SubscriptionId, Azure CLI selected $($accountJson.id)."
    }

    Write-Host "$Label context verified: $($accountJson.name) / $SubscriptionId"
}

function Ensure-SqlServerModule {
    if (-not (Get-Module -ListAvailable -Name SqlServer)) {
        Write-Host 'Installing the Microsoft SqlServer PowerShell module for the current user ...'
        Install-Module SqlServer -Scope CurrentUser -Repository PSGallery -Force -AllowClobber
    }
    Import-Module SqlServer -ErrorAction Stop
    Get-Command Invoke-Sqlcmd -ErrorAction Stop | Out-Null
}

function Get-AdminPassword {
    param([Parameter(Mandatory = $true)][System.Collections.IDictionary]$Boundary)
    $value = (Invoke-AzureCli -Arguments @(
        'keyvault', 'secret', 'show',
        '--vault-name', $Boundary.KeyVault,
        '--name', $sqlAdminPasswordSecretName,
        '--query', 'value',
        '--output', 'tsv',
        '--only-show-errors'
    )).Output

    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Key Vault $($Boundary.KeyVault) returned an empty SQL administrator password."
    }
    return $value
}

function Invoke-AdminSql {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Boundary,
        [Parameter(Mandatory = $true)][string]$AdminPassword,
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Query
    )

    return @(Invoke-Sqlcmd `
        -ServerInstance "$($Boundary.SqlServer).database.windows.net" `
        -Database $Database `
        -Username $sqlAdminLogin `
        -Password $AdminPassword `
        -Query $Query `
        -AbortOnError `
        -ErrorAction Stop)
}

function Invoke-MigrationSql {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Boundary,
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Query
    )

    return @(Invoke-Sqlcmd `
        -ServerInstance "$($Boundary.SqlServer).database.windows.net" `
        -Database $Database `
        -Username $tempLogin `
        -Password $tempPassword `
        -Query $Query `
        -AbortOnError `
        -ErrorAction Stop)
}

function Resolve-ClientIp {
    if (-not [string]::IsNullOrWhiteSpace($ClientIp)) {
        $candidate = $ClientIp.Trim()
    }
    else {
        $candidate = ([string](Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 15)).Trim()
    }

    if ($candidate -notmatch '^([0-9]{1,3}\.){3}[0-9]{1,3}$') {
        throw "Unable to resolve a valid public IPv4 address. Received '$candidate'. Pass -ClientIp explicitly."
    }
    foreach ($octet in $candidate.Split('.')) {
        if ([int]$octet -gt 255) {
            throw "Invalid IPv4 address '$candidate'."
        }
    }
    return $candidate
}

function Add-TemporaryFirewallRule {
    param([Parameter(Mandatory = $true)][System.Collections.IDictionary]$Boundary, [Parameter(Mandatory = $true)][string]$Ip)
    Invoke-AzureCli -Arguments @(
        'sql', 'server', 'firewall-rule', 'create',
        '--resource-group', $Boundary.ResourceGroup,
        '--server', $Boundary.SqlServer,
        '--name', $firewallRuleName,
        '--start-ip-address', $Ip,
        '--end-ip-address', $Ip,
        '--output', 'none',
        '--only-show-errors'
    ) | Out-Null
}

function Remove-TemporaryFirewallRule {
    param([Parameter(Mandatory = $true)][System.Collections.IDictionary]$Boundary)
    $result = Invoke-AzureCli -Arguments @(
        'sql', 'server', 'firewall-rule', 'delete',
        '--resource-group', $Boundary.ResourceGroup,
        '--server', $Boundary.SqlServer,
        '--name', $firewallRuleName,
        '--output', 'none',
        '--only-show-errors'
    ) -AllowFailure
    if ($result.ExitCode -ne 0) {
        Write-Warning "Could not remove temporary SQL firewall rule from $($Boundary.SqlServer). Remove '$firewallRuleName' manually."
    }
}

function Get-WebAppState {
    param([Parameter(Mandatory = $true)][System.Collections.IDictionary]$Boundary)
    return (Invoke-AzureCli -Arguments @(
        'webapp', 'show',
        '--resource-group', $Boundary.ResourceGroup,
        '--name', $Boundary.WebApp,
        '--query', 'state',
        '--output', 'tsv',
        '--only-show-errors'
    )).Output.Trim()
}

function Stop-WebApp {
    param([Parameter(Mandatory = $true)][System.Collections.IDictionary]$Boundary)
    Invoke-AzureCli -Arguments @(
        'webapp', 'stop',
        '--resource-group', $Boundary.ResourceGroup,
        '--name', $Boundary.WebApp,
        '--output', 'none',
        '--only-show-errors'
    ) | Out-Null
}

function Start-WebApp {
    param([Parameter(Mandatory = $true)][System.Collections.IDictionary]$Boundary)
    Invoke-AzureCli -Arguments @(
        'webapp', 'start',
        '--resource-group', $Boundary.ResourceGroup,
        '--name', $Boundary.WebApp,
        '--output', 'none',
        '--only-show-errors'
    ) | Out-Null
}

function New-TemporaryPassword {
    $bytes = New-Object byte[] 24
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $base = [Convert]::ToBase64String($bytes).Replace('/', 'A').Replace('+', 'B').TrimEnd('=')
    return "Aa1!$base"
}

function Get-TableCountMap {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Boundary,
        [Parameter(Mandatory = $true)][string]$Database,
        [switch]$UseMigrationLogin,
        [string]$AdminPassword = ''
    )

    $query = @'
SELECT
    s.name AS SchemaName,
    t.name AS TableName,
    CONVERT(bigint, SUM(p.rows)) AS RowCount
FROM sys.tables AS t
INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id
INNER JOIN sys.partitions AS p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE t.is_ms_shipped = 0
GROUP BY s.name, t.name
ORDER BY s.name, t.name;
'@

    if ($UseMigrationLogin) {
        $rows = Invoke-MigrationSql -Boundary $Boundary -Database $Database -Query $query
    }
    else {
        $rows = Invoke-AdminSql -Boundary $Boundary -AdminPassword $AdminPassword -Database $Database -Query $query
    }

    $map = [ordered]@{}
    foreach ($row in $rows) {
        $key = "$($row.SchemaName).$($row.TableName)"
        $map[$key] = [long]$row.RowCount
    }
    return $map
}

function Compare-TableCounts {
    param([Parameter(Mandatory = $true)]$SourceCounts, [Parameter(Mandatory = $true)]$TargetCounts)

    $keys = @($SourceCounts.Keys + $TargetCounts.Keys | Sort-Object -Unique)
    $mismatches = @()
    foreach ($key in $keys) {
        $sourceValue = if ($SourceCounts.Contains($key)) { [long]$SourceCounts[$key] } else { $null }
        $targetValue = if ($TargetCounts.Contains($key)) { [long]$TargetCounts[$key] } else { $null }
        if ($sourceValue -ne $targetValue) {
            $mismatches += [ordered]@{
                table  = $key
                source = $sourceValue
                target = $targetValue
            }
        }
    }
    return @($mismatches)
}

function Test-DatabaseExists {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Boundary,
        [Parameter(Mandatory = $true)][string]$AdminPassword,
        [Parameter(Mandatory = $true)][string]$DatabaseName
    )
    $literal = ConvertTo-SqlStringLiteral -Value $DatabaseName
    $rows = Invoke-AdminSql -Boundary $Boundary -AdminPassword $AdminPassword -Database 'master' -Query "SELECT CASE WHEN DB_ID($literal) IS NULL THEN 0 ELSE 1 END AS ExistsFlag;"
    return ([int]$rows[0].ExistsFlag -eq 1)
}

function Remove-TemporarySqlPrincipal {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Boundary,
        [Parameter(Mandatory = $true)][string]$AdminPassword,
        [Parameter(Mandatory = $true)][string]$Database
    )

    $loginIdentifier = ConvertTo-SqlIdentifier -Value $tempLogin
    if (Test-DatabaseExists -Boundary $Boundary -AdminPassword $AdminPassword -DatabaseName $Database) {
        try {
            Invoke-AdminSql -Boundary $Boundary -AdminPassword $AdminPassword -Database $Database -Query @"
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$tempLogin')
    DROP USER $loginIdentifier;
"@ | Out-Null
        }
        catch {
            Write-Warning "Could not remove temporary database user from $Database: $($_.Exception.Message)"
        }
    }

    try {
        Invoke-AdminSql -Boundary $Boundary -AdminPassword $AdminPassword -Database 'master' -Query @"
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$tempLogin')
    DROP USER $loginIdentifier;
IF EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = N'$tempLogin')
    DROP LOGIN $loginIdentifier;
"@ | Out-Null
    }
    catch {
        Write-Warning "Could not remove temporary server login from $($Boundary.SqlServer): $($_.Exception.Message)"
    }
}

Assert-GuidValue -Name 'SourceTenantId' -Value $SourceTenantId
Assert-GuidValue -Name 'SourceSubscriptionId' -Value $SourceSubscriptionId
Assert-GuidValue -Name 'TargetTenantId' -Value $TargetTenantId
Assert-GuidValue -Name 'TargetSubscriptionId' -Value $TargetSubscriptionId

if ($SourceTenantId -eq $TargetTenantId) {
    throw 'SourceTenantId and TargetTenantId are identical. This script is reserved for the reviewed cross-tenant Workslip migration.'
}

$expectedConfirmation = if ($Mode -eq 'Final') { 'FINAL COPY NEW TENANT DATABASE' } else { 'SEED NEW TENANT DATABASE' }
if ($Confirmation -ne $expectedConfirmation) {
    throw "Confirmation must be exactly: $expectedConfirmation"
}

foreach ($identifier in @($source.SqlServer, $source.Database, $target.SqlServer, $target.Database, $tempLogin, $copyDatabase, $quarantineDatabase)) {
    Assert-SqlIdentifier -Value $identifier
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI (az) is required.'
}

Ensure-SqlServerModule
$resolvedClientIp = Resolve-ClientIp
$tempPassword = New-TemporaryPassword

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $evidenceDirectory = Join-Path ([System.IO.Path]::GetTempPath()) 'workslip-cutover'
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
    $ManifestPath = Join-Path $evidenceDirectory "sql-$($Mode.ToLowerInvariant())-$timestamp.json"
}
else {
    $manifestDirectory = Split-Path -Parent $ManifestPath
    if (-not [string]::IsNullOrWhiteSpace($manifestDirectory)) {
        New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null
    }
}

Write-Host "Workslip SQL migration mode: $Mode"
Write-Host "Source: $($source.SqlServer)/$($source.Database)"
Write-Host "Target: $($target.SqlServer)/$($target.Database)"
Write-Host "Temporary copy: $copyDatabase"
Write-Host "Operator public IP: $resolvedClientIp"

try {
    Set-AzureContext -TenantId $source.TenantId -SubscriptionId $source.SubscriptionId -Label 'SOURCE'
    $sourceAdminPassword = Get-AdminPassword -Boundary $source
    Add-TemporaryFirewallRule -Boundary $source -Ip $resolvedClientIp
    $sourceWasRunning = ((Get-WebAppState -Boundary $source) -eq 'Running')

    if ($Mode -eq 'Final' -and $sourceWasRunning) {
        Write-Host 'Final mode: stopping current production API before the snapshot ...'
        Stop-WebApp -Boundary $source
    }

    $tempLoginIdentifier = ConvertTo-SqlIdentifier -Value $tempLogin
    $tempPasswordLiteral = ConvertTo-SqlStringLiteral -Value $tempPassword

    Invoke-AdminSql -Boundary $source -AdminPassword $sourceAdminPassword -Database 'master' -Query @"
CREATE LOGIN $tempLoginIdentifier WITH PASSWORD = $tempPasswordLiteral;
CREATE USER $tempLoginIdentifier FOR LOGIN $tempLoginIdentifier WITH DEFAULT_SCHEMA = [dbo];
ALTER ROLE [dbmanager] ADD MEMBER $tempLoginIdentifier;
"@ | Out-Null

    Invoke-AdminSql -Boundary $source -AdminPassword $sourceAdminPassword -Database $source.Database -Query @"
CREATE USER $tempLoginIdentifier FOR LOGIN $tempLoginIdentifier WITH DEFAULT_SCHEMA = [dbo];
ALTER ROLE [db_owner] ADD MEMBER $tempLoginIdentifier;
"@ | Out-Null

    $sidRows = Invoke-AdminSql -Boundary $source -AdminPassword $sourceAdminPassword -Database 'master' -Query @"
SELECT CONVERT(varchar(514), [sid], 1) AS SidHex
FROM sysusers
WHERE [name] = N'$tempLogin';
"@
    if ($sidRows.Count -ne 1 -or [string]::IsNullOrWhiteSpace([string]$sidRows[0].SidHex)) {
        throw 'Could not resolve the temporary migration login SID from the source server.'
    }
    $sidHex = [string]$sidRows[0].SidHex
    if ($sidHex -notmatch '^0x[0-9A-Fa-f]+$') {
        throw 'Source migration login returned an invalid SID.'
    }

    $sourceCountsBeforeCopy = Get-TableCountMap -Boundary $source -Database $source.Database -UseMigrationLogin

    Set-AzureContext -TenantId $target.TenantId -SubscriptionId $target.SubscriptionId -Label 'TARGET'
    $targetAdminPassword = Get-AdminPassword -Boundary $target
    Add-TemporaryFirewallRule -Boundary $target -Ip $resolvedClientIp
    $targetWasRunning = ((Get-WebAppState -Boundary $target) -eq 'Running')

    Invoke-AdminSql -Boundary $target -AdminPassword $targetAdminPassword -Database 'master' -Query @"
CREATE LOGIN $tempLoginIdentifier WITH PASSWORD = $tempPasswordLiteral, SID = $sidHex;
CREATE USER $tempLoginIdentifier FOR LOGIN $tempLoginIdentifier WITH DEFAULT_SCHEMA = [dbo];
ALTER ROLE [dbmanager] ADD MEMBER $tempLoginIdentifier;
"@ | Out-Null

    if (Test-DatabaseExists -Boundary $target -AdminPassword $targetAdminPassword -DatabaseName $copyDatabase) {
        throw "Temporary copy database $copyDatabase already exists. Refusing to overwrite it."
    }

    $copyIdentifier = ConvertTo-SqlIdentifier -Value $copyDatabase
    $sourceServerIdentifier = ConvertTo-SqlIdentifier -Value $source.SqlServer
    $sourceDatabaseIdentifier = ConvertTo-SqlIdentifier -Value $source.Database

    Write-Host 'Starting transactionally consistent cross-tenant Azure SQL copy ...'
    Invoke-MigrationSql -Boundary $target -Database 'master' -Query @"
CREATE DATABASE $copyIdentifier AS COPY OF $sourceServerIdentifier.$sourceDatabaseIdentifier;
"@ | Out-Null
    $copySucceeded = $true

    $copyState = Invoke-AdminSql -Boundary $target -AdminPassword $targetAdminPassword -Database 'master' -Query @"
SELECT state_desc AS StateDescription
FROM sys.databases
WHERE name = N'$copyDatabase';
"@
    if ($copyState.Count -ne 1 -or $copyState[0].StateDescription -ne 'ONLINE') {
        throw "Copied database $copyDatabase is not ONLINE."
    }

    $targetCounts = Get-TableCountMap -Boundary $target -Database $copyDatabase -UseMigrationLogin

    if ($Mode -eq 'Seed') {
        Set-AzureContext -TenantId $source.TenantId -SubscriptionId $source.SubscriptionId -Label 'SOURCE'
        $sourceCountsForEvidence = Get-TableCountMap -Boundary $source -Database $source.Database -UseMigrationLogin
        Set-AzureContext -TenantId $target.TenantId -SubscriptionId $target.SubscriptionId -Label 'TARGET'
    }
    else {
        $sourceCountsForEvidence = $sourceCountsBeforeCopy
    }

    $mismatches = Compare-TableCounts -SourceCounts $sourceCountsForEvidence -TargetCounts $targetCounts
    if ($Mode -eq 'Final' -and $mismatches.Count -ne 0) {
        throw "Final copy row-count validation failed for $($mismatches.Count) table(s). The canonical target database was not changed."
    }
    if ($Mode -eq 'Seed' -and $mismatches.Count -ne 0) {
        Write-Warning "Seed comparison has $($mismatches.Count) row-count difference(s). This can be expected while current production remains writable."
    }

    if ($targetWasRunning) {
        Write-Host 'Stopping new-tenant API for canonical database swap ...'
        Stop-WebApp -Boundary $target
        $targetWasStoppedByScript = $true
    }

    $targetExists = Test-DatabaseExists -Boundary $target -AdminPassword $targetAdminPassword -DatabaseName $target.Database
    if ($targetExists) {
        $targetIdentifier = ConvertTo-SqlIdentifier -Value $target.Database
        $quarantineIdentifier = ConvertTo-SqlIdentifier -Value $quarantineDatabase
        Invoke-AdminSql -Boundary $target -AdminPassword $targetAdminPassword -Database 'master' -Query @"
ALTER DATABASE $targetIdentifier MODIFY NAME = $quarantineIdentifier;
"@ | Out-Null
    }

    $targetIdentifier = ConvertTo-SqlIdentifier -Value $target.Database
    Invoke-AdminSql -Boundary $target -AdminPassword $targetAdminPassword -Database 'master' -Query @"
ALTER DATABASE $copyIdentifier MODIFY NAME = $targetIdentifier;
"@ | Out-Null
    $swapSucceeded = $true

    Invoke-AdminSql -Boundary $target -AdminPassword $targetAdminPassword -Database $target.Database -Query @"
ALTER AUTHORIZATION ON DATABASE::$targetIdentifier TO [$sqlAdminLogin];
"@ | Out-Null

    $manifest = [ordered]@{
        schemaVersion = 1
        generatedUtc = (Get-Date).ToUniversalTime().ToString('o')
        mode = $Mode
        source = [ordered]@{
            server = $source.SqlServer
            database = $source.Database
            tableCounts = $sourceCountsForEvidence
        }
        target = [ordered]@{
            server = $target.SqlServer
            database = $target.Database
            previousDatabaseQuarantine = if ($targetExists) { $quarantineDatabase } else { $null }
            tableCounts = $targetCounts
        }
        validation = [ordered]@{
            exactRequired = ($Mode -eq 'Final')
            mismatchCount = $mismatches.Count
            mismatches = $mismatches
        }
    }
    $manifest | ConvertTo-Json -Depth 10 | Set-Content -Path $ManifestPath -Encoding utf8NoBOM
    $manifestHash = (Get-FileHash -Path $ManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()

    Write-Host 'Data copy and canonical target swap completed.'
    Write-Host "Evidence manifest: $ManifestPath"
    Write-Host "Evidence SHA-256: $manifestHash"

    $operationSucceeded = $true

    if ($Mode -eq 'Final') {
        Write-Warning 'Final mode succeeded: both current production and the new-tenant API remain STOPPED by design until the API/traffic gate.'
    }
}
finally {
    try {
        Set-AzureContext -TenantId $target.TenantId -SubscriptionId $target.SubscriptionId -Label 'TARGET'
        if (-not [string]::IsNullOrWhiteSpace($targetAdminPassword)) {
            $cleanupTargetDb = if ($swapSucceeded) { $target.Database } elseif ($copySucceeded) { $copyDatabase } else { $target.Database }
            Remove-TemporarySqlPrincipal -Boundary $target -AdminPassword $targetAdminPassword -Database $cleanupTargetDb
        }
        Remove-TemporaryFirewallRule -Boundary $target

        if ($targetWasStoppedByScript -and $targetWasRunning -and ($Mode -eq 'Seed' -or -not $operationSucceeded)) {
            Start-WebApp -Boundary $target
        }
    }
    catch {
        Write-Warning "Target cleanup encountered an error: $($_.Exception.Message)"
    }

    try {
        Set-AzureContext -TenantId $source.TenantId -SubscriptionId $source.SubscriptionId -Label 'SOURCE'
        if (-not [string]::IsNullOrWhiteSpace($sourceAdminPassword)) {
            Remove-TemporarySqlPrincipal -Boundary $source -AdminPassword $sourceAdminPassword -Database $source.Database
        }
        Remove-TemporaryFirewallRule -Boundary $source

        if ($Mode -eq 'Final' -and -not $operationSucceeded -and $sourceWasRunning) {
            Write-Warning 'Final copy did not complete cleanly. Frontend traffic is still on current production, so the script is restarting the current production API.'
            Start-WebApp -Boundary $source
        }
    }
    catch {
        Write-Warning "Source cleanup encountered an error: $($_.Exception.Message)"
    }

    $sourceAdminPassword = $null
    $targetAdminPassword = $null
    $tempPassword = $null
}
