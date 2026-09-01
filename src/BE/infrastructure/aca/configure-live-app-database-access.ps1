param(
    [string]$CompanyName = 'mrsoftwarev2',
    [string]$Environment = 'live',
    [string]$RuntimeIdentityName = 'id-workslip-live-app',
    [string]$RuntimeClientId = '',
    [string]$RuntimePrincipalId = ''
)

$ErrorActionPreference = 'Stop'

if ($CompanyName -ne 'mrsoftwarev2' -or $Environment.ToLowerInvariant() -ne 'live') {
    throw 'This script is hard-gated to the mrsoftwarev2/live boundary.'
}

$resourceGroup = 'rg-mrsoftwarev2-live'
$sqlServerName = 'db-mrsoftwarev2-live-server'
$sqlServerFqdn = "$sqlServerName.database.windows.net"
$sqlDatabaseName = 'db-mrsoftwarev2-live'
$firewallRuleName = "AllowLiveAppRuntimeBootstrap-$([Guid]::NewGuid().ToString('N').Substring(0, 12))"

function Invoke-AzureCli {
    param([Parameter(Mandatory = $true)][string[]]$Arguments, [switch]$AllowFailure)

    $output = @(& az @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    $text = ($output -join [Environment]::NewLine).Trim()
    if ($exitCode -ne 0 -and -not $AllowFailure) {
        throw "Azure CLI failed with exit code $exitCode.`n$text"
    }

    [pscustomobject]@{ ExitCode = $exitCode; Output = $text }
}

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw 'Azure CLI is required.'
}
if (-not (Get-Command Invoke-Sqlcmd -ErrorAction SilentlyContinue)) {
    throw 'Invoke-Sqlcmd is required. Install the Microsoft SqlServer PowerShell module first.'
}

$runtimeClientId = $RuntimeClientId.Trim()
$runtimePrincipalId = $RuntimePrincipalId.Trim()
if ([string]::IsNullOrWhiteSpace($runtimeClientId) -or [string]::IsNullOrWhiteSpace($runtimePrincipalId)) {
    $identity = (Invoke-AzureCli -Arguments @(
        'identity', 'show',
        '--resource-group', $resourceGroup,
        '--name', $RuntimeIdentityName,
        '--query', '{clientId:clientId,principalId:principalId}',
        '--output', 'json'
    )).Output | ConvertFrom-Json

    if ([string]::IsNullOrWhiteSpace($runtimeClientId)) {
        $runtimeClientId = [string]$identity.clientId
    }
    if ([string]::IsNullOrWhiteSpace($runtimePrincipalId)) {
        $runtimePrincipalId = [string]$identity.principalId
    }
}

$runtimeClientGuid = [Guid]::Empty
if (-not [Guid]::TryParse($runtimeClientId, [ref]$runtimeClientGuid)) {
    throw "Runtime identity '$RuntimeIdentityName' returned an invalid clientId."
}

$runtimePrincipalGuid = [Guid]::Empty
if (-not [Guid]::TryParse($runtimePrincipalId, [ref]$runtimePrincipalGuid)) {
    throw "Runtime identity '$RuntimeIdentityName' returned an invalid principalId."
}

# For Microsoft Entra service principals created with CREATE USER ... WITH SID,
# Azure SQL expects the service principal client/application ID encoded as the SID.
# The object/principal ID identifies the directory object but does not match the
# token identity Azure SQL uses for this explicit service-principal login mapping.
$escapedRuntimeIdentityName = $RuntimeIdentityName.Replace(']', ']]')
$literalRuntimeIdentityName = $RuntimeIdentityName.Replace("'", "''")
$literalRuntimeClientId = $runtimeClientGuid.ToString('D')

$runnerIp = ([string](Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 30)).Trim()
$parsedIp = $null
if (-not [System.Net.IPAddress]::TryParse($runnerIp, [ref]$parsedIp) -or
    $parsedIp.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
    throw "Could not resolve a valid public IPv4 address. Received '$runnerIp'."
}

$firewallRuleCreated = $false
try {
    Invoke-AzureCli -Arguments @(
        'sql', 'server', 'firewall-rule', 'create',
        '--resource-group', 'rg-mrsoftwarev2-live',
        '--server', $sqlServerName,
        '--name', $firewallRuleName,
        '--start-ip-address', $runnerIp,
        '--end-ip-address', $runnerIp,
        '--only-show-errors', '--output', 'none'
    ) | Out-Null
    $firewallRuleCreated = $true

    $accessToken = (Invoke-AzureCli -Arguments @(
        'account', 'get-access-token',
        '--resource', 'https://database.windows.net/',
        '--query', 'accessToken',
        '--output', 'tsv'
    )).Output.Trim()

    if ([string]::IsNullOrWhiteSpace($accessToken)) {
        throw 'Azure CLI returned an empty Azure SQL access token.'
    }

    $sql = @"
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @runtimeClientId uniqueidentifier = '$literalRuntimeClientId';
DECLARE @expectedSid varbinary(16) = CONVERT(varbinary(16), @runtimeClientId);
DECLARE @sidLiteral varchar(34) = CONVERT(varchar(34), @expectedSid, 1);

IF EXISTS (
    SELECT 1
    FROM sys.database_principals
    WHERE name = N'$literalRuntimeIdentityName'
      AND (sid <> @expectedSid OR type <> 'E')
)
BEGIN
    DROP USER [$escapedRuntimeIdentityName];
END

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$literalRuntimeIdentityName')
BEGIN
    DECLARE @createUser nvarchar(max) =
        N'CREATE USER [$escapedRuntimeIdentityName] WITH SID = ' + @sidLiteral + N', TYPE = E;';
    EXEC sys.sp_executesql @createUser;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.database_principals
    WHERE name = N'$literalRuntimeIdentityName'
      AND sid = @expectedSid
      AND type = 'E'
)
BEGIN
    THROW 51000, 'Runtime Azure SQL service-principal SID does not match the expected client ID.', 1;
END;

IF IS_ROLEMEMBER(N'db_datareader', N'$literalRuntimeIdentityName') <> 1
    ALTER ROLE [db_datareader] ADD MEMBER [$escapedRuntimeIdentityName];

IF IS_ROLEMEMBER(N'db_datawriter', N'$literalRuntimeIdentityName') <> 1
    ALTER ROLE [db_datawriter] ADD MEMBER [$escapedRuntimeIdentityName];
"@

    Invoke-Sqlcmd `
        -ServerInstance $sqlServerFqdn `
        -Database $sqlDatabaseName `
        -AccessToken $accessToken `
        -Query $sql `
        -QueryTimeout 120 `
        -AbortOnError `
        -ErrorAction Stop | Out-Null

    Write-Host "Configured least-privilege SQL access for '$RuntimeIdentityName' using service-principal client ID '$literalRuntimeClientId'." -ForegroundColor Green
}
finally {
    if ($firewallRuleCreated) {
        $deleteResult = Invoke-AzureCli -Arguments @(
            'sql', 'server', 'firewall-rule', 'delete',
            '--resource-group', 'rg-mrsoftwarev2-live',
            '--server', $sqlServerName,
            '--name', $firewallRuleName,
            '--only-show-errors', '--output', 'none'
        ) -AllowFailure

        if ($deleteResult.ExitCode -ne 0) {
            throw "Runtime database access was configured, but temporary firewall cleanup failed for '$firewallRuleName'.`n$($deleteResult.Output)"
        }
    }
}
