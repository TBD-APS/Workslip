[CmdletBinding()]
param(
    [string]$ResourceGroup = 'rg-mrsoftwarev2-live',
    [string]$SqlServerName = 'db-mrsoftwarev2-live-server',
    [string]$SqlDatabaseName = 'db-mrsoftwarev2-live',
    [string]$KeyVaultName = 'kv-mrsoftwarev2-live',
    [string]$MigrationIdentityName = 'id-mrsoftwarev2-live-migration'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$secretName = 'Azure--Sql--AdminPassword'
$firewallRuleName = "AllowLiveMigrationBootstrap-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"

function Invoke-Az {
    param([Parameter(Mandatory = $true)][string[]]$Arguments, [switch]$AllowFailure)
    $lines = @(& az @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    $text = ($lines -join [Environment]::NewLine).Trim()
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "Azure CLI failed with exit code $exitCode.`n$text"
    }
    return [pscustomobject]@{ ExitCode = $exitCode; Output = $text }
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) { throw 'Azure CLI is required.' }
if (-not (Get-Command Invoke-Sqlcmd -ErrorAction SilentlyContinue)) { throw 'Invoke-Sqlcmd is required.' }

$identity = (Invoke-Az -Arguments @(
    'identity', 'show',
    '--resource-group', $ResourceGroup,
    '--name', $MigrationIdentityName,
    '--query', '{clientId:clientId,principalId:principalId}',
    '--output', 'json', '--only-show-errors'
)).Output | ConvertFrom-Json

$migrationGuid = [Guid]::Empty
if (-not [Guid]::TryParse([string]$identity.clientId, [ref]$migrationGuid)) {
    throw "Migration identity '$MigrationIdentityName' returned an invalid client ID."
}

$adminPassword = (Invoke-Az -Arguments @(
    'keyvault', 'secret', 'show',
    '--vault-name', $KeyVaultName,
    '--name', $secretName,
    '--query', 'value', '--output', 'tsv', '--only-show-errors'
)).Output.Trim()
if ([string]::IsNullOrWhiteSpace($adminPassword)) { throw 'Live SQL administrator password is unavailable.' }

$runnerIp = ([string](Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 30)).Trim()
$parsedIp = $null
if (-not [System.Net.IPAddress]::TryParse($runnerIp, [ref]$parsedIp) -or
    $parsedIp.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
    throw "Invalid runner IPv4 '$runnerIp'."
}

$firewallCreated = $false
try {
    Invoke-Az -Arguments @(
        'sql', 'server', 'firewall-rule', 'create',
        '--resource-group', $ResourceGroup,
        '--server', $SqlServerName,
        '--name', $firewallRuleName,
        '--start-ip-address', $runnerIp,
        '--end-ip-address', $runnerIp,
        '--output', 'none', '--only-show-errors'
    ) | Out-Null
    $firewallCreated = $true

    $escapedName = $MigrationIdentityName.Replace(']', ']]')
    $literalName = $MigrationIdentityName.Replace("'", "''")
    $literalClientId = $migrationGuid.ToString('D')
    $sql = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @clientId uniqueidentifier = '$literalClientId';
DECLARE @expectedSid varbinary(16) = CONVERT(varbinary(16), @clientId);
DECLARE @sidLiteral varchar(34) = CONVERT(varchar(34), @expectedSid, 1);

IF EXISTS (
    SELECT 1 FROM sys.database_principals
    WHERE name = N'$literalName' AND (sid <> @expectedSid OR type <> 'E')
)
BEGIN
    DROP USER [$escapedName];
END;

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$literalName')
BEGIN
    DECLARE @createUser nvarchar(max) = N'CREATE USER [$escapedName] WITH SID = ' + @sidLiteral + N', TYPE = E;';
    EXEC sys.sp_executesql @createUser;
END;

IF IS_ROLEMEMBER(N'db_owner', N'$literalName') <> 1 ALTER ROLE [db_owner] ADD MEMBER [$escapedName];
IF IS_ROLEMEMBER(N'db_ddladmin', N'$literalName') <> 1 ALTER ROLE [db_ddladmin] ADD MEMBER [$escapedName];
IF IS_ROLEMEMBER(N'db_accessadmin', N'$literalName') <> 1 ALTER ROLE [db_accessadmin] ADD MEMBER [$escapedName];
IF IS_ROLEMEMBER(N'db_securityadmin', N'$literalName') <> 1 ALTER ROLE [db_securityadmin] ADD MEMBER [$escapedName];
IF IS_ROLEMEMBER(N'db_datareader', N'$literalName') <> 1 ALTER ROLE [db_datareader] ADD MEMBER [$escapedName];
IF IS_ROLEMEMBER(N'db_datawriter', N'$literalName') <> 1 ALTER ROLE [db_datawriter] ADD MEMBER [$escapedName];
"@

    $parameters = @{
        ServerInstance = "$SqlServerName.database.windows.net"
        Database = $SqlDatabaseName
        Username = 'rbj'
        Password = $adminPassword
        Query = $sql
        QueryTimeout = 120
        AbortOnError = $true
        ErrorAction = 'Stop'
    }
    Invoke-Sqlcmd @parameters | Out-Null
    Write-Host "Configured migration SQL identity '$MigrationIdentityName' on '$SqlDatabaseName'." -ForegroundColor Green
}
finally {
    $adminPassword = $null
    if ($firewallCreated) {
        $delete = Invoke-Az -Arguments @(
            'sql', 'server', 'firewall-rule', 'delete',
            '--resource-group', $ResourceGroup,
            '--server', $SqlServerName,
            '--name', $firewallRuleName,
            '--output', 'none', '--only-show-errors'
        ) -AllowFailure
        if ($delete.ExitCode -ne 0) {
            throw "Migration identity was configured but temporary firewall cleanup failed.`n$($delete.Output)"
        }
    }
}
