[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][Guid]$SourceTenantId,
    [Parameter(Mandatory = $true)][Guid]$SourceSubscriptionId,
    [Parameter(Mandatory = $true)][Guid]$SourceClientId,
    [Parameter(Mandatory = $true)][Guid]$TargetTenantId,
    [Parameter(Mandatory = $true)][Guid]$TargetSubscriptionId,
    [Parameter(Mandatory = $true)][Guid]$TargetClientId,
    [Parameter(Mandatory = $true)][string]$FederatedTokenPath,
    [Parameter(Mandatory = $true)][ValidateSet('SEED NEW TENANT DATABASE')][string]$Confirmation,
    [string]$ManifestPath = '',
    [string]$ClientIp = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$source = [ordered]@{
    TenantId       = $SourceTenantId.ToString('D')
    SubscriptionId = $SourceSubscriptionId.ToString('D')
    ClientId       = $SourceClientId.ToString('D')
    ResourceGroup  = 'rg-mrsoftware-prod'
    SqlServer      = 'db-mrsoftware-prod-server'
    Database       = 'db-mrsoftware-prod'
    KeyVault       = 'kv-mrsoftware-prod'
}
$target = [ordered]@{
    TenantId        = $TargetTenantId.ToString('D')
    SubscriptionId  = $TargetSubscriptionId.ToString('D')
    ClientId        = $TargetClientId.ToString('D')
    ResourceGroup   = 'rg-mrsoftwarev2-live'
    SqlServer       = 'db-mrsoftwarev2-live-server'
    Database        = 'db-mrsoftwarev2-live'
    KeyVault        = 'kv-mrsoftwarev2-live'
    WebApp          = 'api-mrsoftwarev2-live'
    RuntimeIdentity = 'id-workslip-live-app'
}

$sqlAdminLogin = 'rbj'
$sqlAdminPasswordSecretName = 'Azure--Sql--AdminPassword'
$timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddHHmmss')
$tempLogin = "workslip_seed_$timestamp"
$copyDatabase = "$($target.Database)-seed-$timestamp"
$quarantineDatabase = "$($target.Database)-preseed-$timestamp"
$failedDatabase = "$($target.Database)-failed-$timestamp"
$firewallRuleName = "AllowWorkslipSeed-$timestamp"
$sourceAdminPassword = $null
$targetAdminPassword = $null
$tempPassword = $null
$sourceFirewallCreated = $false
$targetFirewallCreated = $false
$targetWasRunning = $false
$targetWasStopped = $false
$targetQuarantined = $false
$swapSucceeded = $false
$operationSucceeded = $false

function Invoke-Az {
    param([Parameter(Mandatory = $true)][string[]]$Arguments, [switch]$AllowFailure)
    $lines = @(& az @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    $text = ($lines -join [Environment]::NewLine).Trim()
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        if ([string]::IsNullOrWhiteSpace($text)) { $text = 'Azure CLI returned no diagnostic output.' }
        throw "Azure CLI failed with exit code $exitCode.`n$text"
    }
    return [pscustomobject]@{ ExitCode = $exitCode; Output = $text }
}

function Connect-Boundary {
    param([Parameter(Mandatory = $true)][System.Collections.IDictionary]$Boundary, [Parameter(Mandatory = $true)][string]$Label)

    $token = (Get-Content -LiteralPath $FederatedTokenPath -Raw -ErrorAction Stop).Trim()
    if ([string]::IsNullOrWhiteSpace($token)) { throw 'GitHub OIDC token file is empty.' }

    Invoke-Az -Arguments @('account', 'clear') -AllowFailure | Out-Null
    Invoke-Az -Arguments @(
        'login', '--service-principal',
        '--username', $Boundary.ClientId,
        '--tenant', $Boundary.TenantId,
        '--federated-token', $token,
        '--allow-no-subscriptions',
        '--output', 'none',
        '--only-show-errors'
    ) | Out-Null
    Invoke-Az -Arguments @('account', 'set', '--subscription', $Boundary.SubscriptionId) | Out-Null

    $account = (Invoke-Az -Arguments @('account', 'show', '--query', '{tenantId:tenantId,subscriptionId:id}', '--output', 'json')).Output | ConvertFrom-Json
    if ([string]$account.tenantId -ne $Boundary.TenantId -or [string]$account.subscriptionId -ne $Boundary.SubscriptionId) {
        throw "$Label Azure boundary mismatch after OIDC login."
    }
    Write-Host "$Label Azure boundary verified." -ForegroundColor Cyan
}

function Ensure-SqlModule {
    if (-not (Get-Module -ListAvailable -Name SqlServer)) {
        Install-Module SqlServer -Scope CurrentUser -Repository PSGallery -Force -AllowClobber
    }
    Import-Module SqlServer -ErrorAction Stop
    Get-Command Invoke-Sqlcmd -ErrorAction Stop | Out-Null
}

function Resolve-ClientIp {
    if (-not [string]::IsNullOrWhiteSpace($ClientIp)) { $candidate = $ClientIp.Trim() }
    else { $candidate = ([string](Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 30)).Trim() }
    $parsed = $null
    if (-not [System.Net.IPAddress]::TryParse($candidate, [ref]$parsed) -or
        $parsed.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
        throw "Unable to resolve a valid public IPv4 address. Received '$candidate'."
    }
    return $candidate
}

function New-TemporaryPassword {
    $bytes = New-Object byte[] 24
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $value = [Convert]::ToBase64String($bytes).Replace('/', 'A').Replace('+', 'B').TrimEnd('=')
    return "Aa1!$value"
}

function ConvertTo-SqlIdentifier {
    param([Parameter(Mandatory = $true)][string]$Value)
    if ($Value -notmatch '^[A-Za-z0-9_-]+$') { throw "Unsafe SQL identifier '$Value'." }
    return "[$Value]"
}

function ConvertTo-SqlStringLiteral {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Value)
    return "N'$($Value.Replace("'", "''"))'"
}

function Get-AdminPassword {
    param([Parameter(Mandatory = $true)][System.Collections.IDictionary]$Boundary)
    $value = (Invoke-Az -Arguments @(
        'keyvault', 'secret', 'show',
        '--vault-name', $Boundary.KeyVault,
        '--name', $sqlAdminPasswordSecretName,
        '--query', 'value', '--output', 'tsv', '--only-show-errors'
    )).Output.Trim()
    if ([string]::IsNullOrWhiteSpace($value)) { throw "Key Vault $($Boundary.KeyVault) returned an empty SQL administrator password." }
    return $value
}

function Add-Firewall {
    param([Parameter(Mandatory = $true)][System.Collections.IDictionary]$Boundary, [Parameter(Mandatory = $true)][string]$Ip)
    Invoke-Az -Arguments @(
        'sql', 'server', 'firewall-rule', 'create',
        '--resource-group', $Boundary.ResourceGroup,
        '--server', $Boundary.SqlServer,
        '--name', $firewallRuleName,
        '--start-ip-address', $Ip,
        '--end-ip-address', $Ip,
        '--output', 'none', '--only-show-errors'
    ) | Out-Null
}

function Remove-Firewall {
    param([Parameter(Mandatory = $true)][System.Collections.IDictionary]$Boundary)
    Invoke-Az -Arguments @(
        'sql', 'server', 'firewall-rule', 'delete',
        '--resource-group', $Boundary.ResourceGroup,
        '--server', $Boundary.SqlServer,
        '--name', $firewallRuleName,
        '--output', 'none', '--only-show-errors'
    ) -AllowFailure | Out-Null
}

function Invoke-AdminSql {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Boundary,
        [Parameter(Mandatory = $true)][string]$Password,
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Query,
        [int]$QueryTimeout = 180
    )
    $parameters = @{
        ServerInstance = "$($Boundary.SqlServer).database.windows.net"
        Database = $Database
        Username = $sqlAdminLogin
        Password = $Password
        Query = $Query
        QueryTimeout = $QueryTimeout
        AbortOnError = $true
        ErrorAction = 'Stop'
    }
    return @(Invoke-Sqlcmd @parameters)
}

function Invoke-SeedSql {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Boundary,
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Query,
        [int]$QueryTimeout = 1800
    )
    $parameters = @{
        ServerInstance = "$($Boundary.SqlServer).database.windows.net"
        Database = $Database
        Username = $tempLogin
        Password = $tempPassword
        Query = $Query
        QueryTimeout = $QueryTimeout
        AbortOnError = $true
        ErrorAction = 'Stop'
    }
    return @(Invoke-Sqlcmd @parameters)
}

function Test-DatabaseExists {
    param([System.Collections.IDictionary]$Boundary, [string]$Password, [string]$DatabaseName)
    $literal = ConvertTo-SqlStringLiteral $DatabaseName
    $rows = Invoke-AdminSql -Boundary $Boundary -Password $Password -Database 'master' -Query "SELECT CASE WHEN DB_ID($literal) IS NULL THEN 0 ELSE 1 END AS ExistsFlag;"
    return ($rows.Count -gt 0 -and [int]$rows[0].ExistsFlag -eq 1)
}

function Get-TableCounts {
    param([System.Collections.IDictionary]$Boundary, [string]$Database, [string]$Password = '', [switch]$UseSeedLogin)
    $query = @'
SELECT s.name AS SchemaName, t.name AS TableName, CONVERT(bigint, SUM(p.rows)) AS RowCount
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1)
WHERE t.is_ms_shipped = 0
GROUP BY s.name, t.name
ORDER BY s.name, t.name;
'@
    $rows = if ($UseSeedLogin) { Invoke-SeedSql -Boundary $Boundary -Database $Database -Query $query } else { Invoke-AdminSql -Boundary $Boundary -Password $Password -Database $Database -Query $query }
    $map = [ordered]@{}
    foreach ($row in $rows) { $map["$($row.SchemaName).$($row.TableName)"] = [long]$row.RowCount }
    return $map
}

function Compare-Counts {
    param($SourceCounts, $TargetCounts)
    $result = @()
    foreach ($key in @($SourceCounts.Keys + $TargetCounts.Keys | Sort-Object -Unique)) {
        $s = if ($SourceCounts.Contains($key)) { [long]$SourceCounts[$key] } else { $null }
        $t = if ($TargetCounts.Contains($key)) { [long]$TargetCounts[$key] } else { $null }
        if ($s -ne $t) { $result += [ordered]@{ table = $key; source = $s; target = $t } }
    }
    return @($result)
}

function Remove-SeedPrincipal {
    param([System.Collections.IDictionary]$Boundary, [string]$Password, [string]$Database)
    $identifier = ConvertTo-SqlIdentifier $tempLogin
    if (Test-DatabaseExists -Boundary $Boundary -Password $Password -DatabaseName $Database) {
        try {
            Invoke-AdminSql -Boundary $Boundary -Password $Password -Database $Database -Query "IF USER_ID(N'$tempLogin') IS NOT NULL DROP USER $identifier;" | Out-Null
        } catch { Write-Warning "Could not remove temporary user from ${Database}: $($_.Exception.Message)" }
    }
    try {
        Invoke-AdminSql -Boundary $Boundary -Password $Password -Database 'master' -Query @"
IF USER_ID(N'$tempLogin') IS NOT NULL DROP USER $identifier;
IF EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = N'$tempLogin') DROP LOGIN $identifier;
"@ | Out-Null
    } catch { Write-Warning "Could not remove temporary login from $($Boundary.SqlServer): $($_.Exception.Message)" }
}

function Get-WebAppRunning {
    $result = Invoke-Az -Arguments @('webapp', 'show', '--resource-group', $target.ResourceGroup, '--name', $target.WebApp, '--query', 'state', '--output', 'tsv', '--only-show-errors') -AllowFailure
    return ($result.ExitCode -eq 0 -and $result.Output.Trim() -eq 'Running')
}

function Stop-TargetWebApp {
    Invoke-Az -Arguments @('webapp', 'stop', '--resource-group', $target.ResourceGroup, '--name', $target.WebApp, '--output', 'none', '--only-show-errors') | Out-Null
}

function Start-TargetWebApp {
    Invoke-Az -Arguments @('webapp', 'start', '--resource-group', $target.ResourceGroup, '--name', $target.WebApp, '--output', 'none', '--only-show-errors') | Out-Null
}

if ($Confirmation -ne 'SEED NEW TENANT DATABASE') { throw 'Invalid seed confirmation.' }
if ($SourceTenantId -eq $TargetTenantId) { throw 'Source and target tenants must be different.' }
if (-not (Test-Path -LiteralPath $FederatedTokenPath -PathType Leaf)) { throw 'Federated OIDC token file does not exist.' }
if (-not (Get-Command az -ErrorAction SilentlyContinue)) { throw 'Azure CLI is required.' }
Ensure-SqlModule

$resolvedIp = Resolve-ClientIp
$tempPassword = New-TemporaryPassword
if ([string]::IsNullOrWhiteSpace($ManifestPath)) { $ManifestPath = Join-Path ([System.IO.Path]::GetTempPath()) "workslip-sql-seed-$timestamp.json" }
$manifestDirectory = Split-Path -Parent $ManifestPath
if (-not [string]::IsNullOrWhiteSpace($manifestDirectory)) { New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null }

Write-Host 'Starting guarded Workslip cross-tenant SQL seed.' -ForegroundColor Cyan
Write-Host "Source: $($source.SqlServer)/$($source.Database)"
Write-Host "Target: $($target.SqlServer)/$($target.Database)"
Write-Host "Temporary copy: $copyDatabase"

try {
    Connect-Boundary -Boundary $source -Label 'SOURCE'
    $sourceAdminPassword = Get-AdminPassword -Boundary $source
    Add-Firewall -Boundary $source -Ip $resolvedIp
    $sourceFirewallCreated = $true

    $tempIdentifier = ConvertTo-SqlIdentifier $tempLogin
    $tempPasswordLiteral = ConvertTo-SqlStringLiteral $tempPassword
    Invoke-AdminSql -Boundary $source -Password $sourceAdminPassword -Database 'master' -Query @"
CREATE LOGIN $tempIdentifier WITH PASSWORD = $tempPasswordLiteral;
CREATE USER $tempIdentifier FOR LOGIN $tempIdentifier WITH DEFAULT_SCHEMA = [dbo];
ALTER ROLE [dbmanager] ADD MEMBER $tempIdentifier;
"@ | Out-Null
    Invoke-AdminSql -Boundary $source -Password $sourceAdminPassword -Database $source.Database -Query @"
CREATE USER $tempIdentifier FOR LOGIN $tempIdentifier WITH DEFAULT_SCHEMA = [dbo];
ALTER ROLE [db_owner] ADD MEMBER $tempIdentifier;
"@ | Out-Null

    $sidRows = Invoke-AdminSql -Boundary $source -Password $sourceAdminPassword -Database 'master' -Query "SELECT CONVERT(varchar(514), [sid], 1) AS SidHex FROM sysusers WHERE [name] = N'$tempLogin';"
    if ($sidRows.Count -ne 1 -or [string]$sidRows[0].SidHex -notmatch '^0x[0-9A-Fa-f]+$') { throw 'Could not resolve a valid temporary SQL login SID.' }
    $sidHex = [string]$sidRows[0].SidHex
    $sourceCountsBeforeCopy = Get-TableCounts -Boundary $source -Database $source.Database -UseSeedLogin

    Connect-Boundary -Boundary $target -Label 'TARGET'
    $targetAdminPassword = Get-AdminPassword -Boundary $target
    Add-Firewall -Boundary $target -Ip $resolvedIp
    $targetFirewallCreated = $true

    Invoke-AdminSql -Boundary $target -Password $targetAdminPassword -Database 'master' -Query @"
CREATE LOGIN $tempIdentifier WITH PASSWORD = $tempPasswordLiteral, SID = $sidHex;
CREATE USER $tempIdentifier FOR LOGIN $tempIdentifier WITH DEFAULT_SCHEMA = [dbo];
ALTER ROLE [dbmanager] ADD MEMBER $tempIdentifier;
"@ | Out-Null

    if (Test-DatabaseExists -Boundary $target -Password $targetAdminPassword -DatabaseName $copyDatabase) { throw "Seed database '$copyDatabase' already exists." }
    $copyIdentifier = ConvertTo-SqlIdentifier $copyDatabase
    $sourceServerIdentifier = ConvertTo-SqlIdentifier $source.SqlServer
    $sourceDatabaseIdentifier = ConvertTo-SqlIdentifier $source.Database
    Write-Host 'Creating transactionally consistent cross-tenant database copy ...' -ForegroundColor Cyan
    Invoke-SeedSql -Boundary $target -Database 'master' -Query "CREATE DATABASE $copyIdentifier AS COPY OF $sourceServerIdentifier.$sourceDatabaseIdentifier;" -QueryTimeout 3600 | Out-Null

    $state = Invoke-AdminSql -Boundary $target -Password $targetAdminPassword -Database 'master' -Query "SELECT state_desc AS StateDescription FROM sys.databases WHERE name = N'$copyDatabase';"
    if ($state.Count -ne 1 -or [string]$state[0].StateDescription -ne 'ONLINE') { throw "Seed database '$copyDatabase' is not ONLINE." }
    $targetCounts = Get-TableCounts -Boundary $target -Database $copyDatabase -UseSeedLogin

    Connect-Boundary -Boundary $source -Label 'SOURCE'
    $sourceCountsForEvidence = Get-TableCounts -Boundary $source -Database $source.Database -UseSeedLogin
    $mismatches = Compare-Counts -SourceCounts $sourceCountsForEvidence -TargetCounts $targetCounts

    Connect-Boundary -Boundary $target -Label 'TARGET'
    $targetWasRunning = Get-WebAppRunning
    if ($targetWasRunning) {
        Write-Host 'Stopping compatibility App Service for the canonical database swap ...'
        Stop-TargetWebApp
        $targetWasStopped = $true
    }

    $targetExists = Test-DatabaseExists -Boundary $target -Password $targetAdminPassword -DatabaseName $target.Database
    if ($targetExists) {
        $targetIdentifier = ConvertTo-SqlIdentifier $target.Database
        $quarantineIdentifier = ConvertTo-SqlIdentifier $quarantineDatabase
        Invoke-AdminSql -Boundary $target -Password $targetAdminPassword -Database 'master' -Query @"
ALTER DATABASE $targetIdentifier SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
ALTER DATABASE $targetIdentifier MODIFY NAME = $quarantineIdentifier;
ALTER DATABASE $quarantineIdentifier SET MULTI_USER;
"@ | Out-Null
        $targetQuarantined = $true
    }

    $canonicalIdentifier = ConvertTo-SqlIdentifier $target.Database
    Invoke-AdminSql -Boundary $target -Password $targetAdminPassword -Database 'master' -Query "ALTER DATABASE $copyIdentifier MODIFY NAME = $canonicalIdentifier;" | Out-Null
    $swapSucceeded = $true
    Invoke-AdminSql -Boundary $target -Password $targetAdminPassword -Database $target.Database -Query "ALTER AUTHORIZATION ON DATABASE::$canonicalIdentifier TO [$sqlAdminLogin];" | Out-Null

    $runtimeClientId = (Invoke-Az -Arguments @('identity', 'show', '--resource-group', $target.ResourceGroup, '--name', $target.RuntimeIdentity, '--query', 'clientId', '--output', 'tsv', '--only-show-errors')).Output.Trim()
    $runtimeGuid = [Guid]::Empty
    if (-not [Guid]::TryParse($runtimeClientId, [ref]$runtimeGuid)) { throw 'Target ACA runtime identity returned an invalid client ID.' }
    $escapedRuntimeName = $target.RuntimeIdentity.Replace(']', ']]')
    $literalRuntimeName = $target.RuntimeIdentity.Replace("'", "''")
    $literalRuntimeClientId = $runtimeGuid.ToString('D')
    Invoke-AdminSql -Boundary $target -Password $targetAdminPassword -Database $target.Database -Query @"
SET NOCOUNT ON;
DECLARE @runtimeClientId uniqueidentifier = '$literalRuntimeClientId';
DECLARE @expectedSid varbinary(16) = CONVERT(varbinary(16), @runtimeClientId);
DECLARE @sidLiteral varchar(34) = CONVERT(varchar(34), @expectedSid, 1);
IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$literalRuntimeName' AND (sid <> @expectedSid OR type <> 'E'))
    DROP USER [$escapedRuntimeName];
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$literalRuntimeName')
BEGIN
    DECLARE @sql nvarchar(max) = N'CREATE USER [$escapedRuntimeName] WITH SID = ' + @sidLiteral + N', TYPE = E;';
    EXEC sys.sp_executesql @sql;
END;
IF IS_ROLEMEMBER(N'db_datareader', N'$literalRuntimeName') <> 1 ALTER ROLE [db_datareader] ADD MEMBER [$escapedRuntimeName];
IF IS_ROLEMEMBER(N'db_datawriter', N'$literalRuntimeName') <> 1 ALTER ROLE [db_datawriter] ADD MEMBER [$escapedRuntimeName];
"@ | Out-Null

    $usersCount = if ($targetCounts.Contains('dbo.Users')) { [long]$targetCounts['dbo.Users'] } else { 0L }
    $manifest = [ordered]@{
        schemaVersion = 1
        generatedUtc = (Get-Date).ToUniversalTime().ToString('o')
        mode = 'Seed'
        source = [ordered]@{ server = $source.SqlServer; database = $source.Database; tableCounts = $sourceCountsForEvidence }
        target = [ordered]@{ server = $target.SqlServer; database = $target.Database; previousDatabaseQuarantine = if ($targetQuarantined) { $quarantineDatabase } else { $null }; tableCounts = $targetCounts }
        validation = [ordered]@{ exactRequired = $false; mismatchCount = $mismatches.Count; mismatches = $mismatches; usersRowCount = $usersCount }
    }
    $manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $ManifestPath -Encoding utf8NoBOM
    $hash = (Get-FileHash -LiteralPath $ManifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "Seed copy complete. Users=$usersCount, row-count differences=$($mismatches.Count), evidence SHA-256=$hash" -ForegroundColor Green
    if ($mismatches.Count -gt 0) { Write-Warning 'Source stayed writable during seed, so row-count drift can occur. Final cutover still requires an exact frozen copy.' }
    $operationSucceeded = $true
}
catch {
    Write-Error $_
    throw
}
finally {
    try {
        Connect-Boundary -Boundary $target -Label 'TARGET-CLEANUP'
        if (-not [string]::IsNullOrWhiteSpace($targetAdminPassword)) {
            if (-not $operationSucceeded -and $swapSucceeded -and $targetQuarantined) {
                Write-Warning 'Seed failed after canonical swap; restoring the previous target database.'
                $canonicalIdentifier = ConvertTo-SqlIdentifier $target.Database
                $failedIdentifier = ConvertTo-SqlIdentifier $failedDatabase
                $quarantineIdentifier = ConvertTo-SqlIdentifier $quarantineDatabase
                Invoke-AdminSql -Boundary $target -Password $targetAdminPassword -Database 'master' -Query @"
ALTER DATABASE $canonicalIdentifier SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
ALTER DATABASE $canonicalIdentifier MODIFY NAME = $failedIdentifier;
ALTER DATABASE $failedIdentifier SET MULTI_USER;
ALTER DATABASE $quarantineIdentifier MODIFY NAME = $canonicalIdentifier;
"@ | Out-Null
                $swapSucceeded = $false
                $targetQuarantined = $false
                try { Invoke-AdminSql -Boundary $target -Password $targetAdminPassword -Database 'master' -Query "DROP DATABASE $failedIdentifier;" | Out-Null } catch { Write-Warning "Failed-copy cleanup warning: $($_.Exception.Message)" }
            }
            $cleanupDb = if ($swapSucceeded) { $target.Database } elseif (Test-DatabaseExists -Boundary $target -Password $targetAdminPassword -DatabaseName $copyDatabase) { $copyDatabase } else { $target.Database }
            Remove-SeedPrincipal -Boundary $target -Password $targetAdminPassword -Database $cleanupDb
            if (-not $operationSucceeded -and -not $swapSucceeded -and (Test-DatabaseExists -Boundary $target -Password $targetAdminPassword -DatabaseName $copyDatabase)) {
                $copyIdentifier = ConvertTo-SqlIdentifier $copyDatabase
                try { Invoke-AdminSql -Boundary $target -Password $targetAdminPassword -Database 'master' -Query "DROP DATABASE $copyIdentifier;" | Out-Null } catch { Write-Warning "Temporary copy cleanup warning: $($_.Exception.Message)" }
            }
        }
        if ($targetFirewallCreated) { Remove-Firewall -Boundary $target }
        if ($targetWasStopped -and $targetWasRunning) { Start-TargetWebApp }
    } catch { Write-Warning "Target cleanup warning: $($_.Exception.Message)" }

    try {
        Connect-Boundary -Boundary $source -Label 'SOURCE-CLEANUP'
        if (-not [string]::IsNullOrWhiteSpace($sourceAdminPassword)) { Remove-SeedPrincipal -Boundary $source -Password $sourceAdminPassword -Database $source.Database }
        if ($sourceFirewallCreated) { Remove-Firewall -Boundary $source }
    } catch { Write-Warning "Source cleanup warning: $($_.Exception.Message)" }

    Invoke-Az -Arguments @('account', 'clear') -AllowFailure | Out-Null
    $sourceAdminPassword = $null
    $targetAdminPassword = $null
    $tempPassword = $null
}
