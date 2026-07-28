param(
    [Parameter(Mandatory = $true)]
    [string]$Environment,
    [Parameter(Mandatory = $true)]
    [string]$CompanyName,
    [Parameter(Mandatory = $true)]
    [string]$SqlAdminPassword
)

$ErrorActionPreference = 'Stop'

function Initialize-AzureCliInvocation {
    $azureCliCommand = Get-Command az -CommandType Application -ErrorAction Stop |
        Select-Object -First 1

    if ($null -eq $azureCliCommand -or [string]::IsNullOrWhiteSpace($azureCliCommand.Source)) {
        throw 'Could not resolve Azure CLI.'
    }

    $script:AzureCliExecutable = $azureCliCommand.Source
    $script:AzureCliPrefix = @()

    $runningOnWindows = [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT
    if (-not $runningOnWindows) {
        return
    }

    $azureCliDirectory = Split-Path -Parent $azureCliCommand.Source
    $pythonCandidate = [System.IO.Path]::GetFullPath(
        (Join-Path $azureCliDirectory '..\python.exe')
    )

    if (-not (Test-Path $pythonCandidate)) {
        throw "Azure CLI Python runtime not found: $pythonCandidate"
    }

    $script:AzureCliExecutable = $pythonCandidate
    $script:AzureCliPrefix = @('-IBm', 'azure.cli')
}

function Invoke-AzureCli {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments,
        [switch]$AllowFailure
    )

    $commandArguments = @($script:AzureCliPrefix) + @($Arguments)
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'

    try {
        $output = @(
            & $script:AzureCliExecutable @commandArguments 2>&1 |
                ForEach-Object { $_.ToString() }
        )
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

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

$sqlcmdCommand = Get-Command sqlcmd -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($null -eq $sqlcmdCommand -or [string]::IsNullOrWhiteSpace($sqlcmdCommand.Source)) {
    throw 'sqlcmd is required to provision SQL access. Install Microsoft sqlcmd, then rerun deploy-infrastructure.ps1.'
}

Initialize-AzureCliInvocation

$normalizedEnvironment = $Environment.ToLowerInvariant()
$resourceGroup = "rg-$CompanyName-$normalizedEnvironment"
$sqlServerName = "db-$CompanyName-$normalizedEnvironment-server"
$sqlServerFqdn = "$sqlServerName.database.windows.net"
$sqlDatabaseName = "db-$CompanyName-$normalizedEnvironment"
$identityName = "id-$CompanyName-$normalizedEnvironment"
$firewallRuleName = 'AllowSqlProvisioningScript'
$sqlFile = New-TemporaryFile
$firewallRuleCreated = $false
$sqlProvisioningSucceeded = $false
$previousSqlCmdPassword = $env:SQLCMDPASSWORD

try {
    $identityResult = Invoke-AzureCli -Arguments @(
        'identity', 'show',
        '--resource-group', $resourceGroup,
        '--name', $identityName,
        '--query', 'clientId',
        '--only-show-errors',
        '--output', 'tsv'
    )

    $clientId = $identityResult.Output.Trim()
    $parsedClientId = [Guid]::Empty
    if (-not [Guid]::TryParse($clientId, [ref]$parsedClientId)) {
        throw "Managed identity '$identityName' returned an invalid client ID: '$clientId'."
    }

    $sid = '0x' + (($parsedClientId.ToByteArray() | ForEach-Object { $_.ToString('X2') }) -join '')
    $provisioningIp = ([string](Invoke-RestMethod -Uri 'https://api.ipify.org' -TimeoutSec 30)).Trim()
    $parsedIp = $null

    if (-not [System.Net.IPAddress]::TryParse($provisioningIp, [ref]$parsedIp) -or
        $parsedIp.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
        throw "Could not resolve a valid public IPv4 address. Received '$provisioningIp'."
    }

    Invoke-AzureCli -Arguments @(
        'sql', 'server', 'firewall-rule', 'create',
        '--resource-group', $resourceGroup,
        '--server', $sqlServerName,
        '--name', $firewallRuleName,
        '--start-ip-address', $provisioningIp,
        '--end-ip-address', $provisioningIp,
        '--only-show-errors',
        '--output', 'none'
    ) | Out-Null
    $firewallRuleCreated = $true

    $sql = @"
DECLARE @userName sysname = N'$identityName';
DECLARE @expectedSid varbinary(16) = $sid;
DECLARE @currentSid varbinary(85) = (
    SELECT sid
    FROM sys.database_principals
    WHERE name = @userName
);
DECLARE @sql nvarchar(max);

-- A recreated user-assigned identity has a new client ID. Replace a stale
-- contained user before granting roles so runtime login cannot silently target
-- the previous identity.
IF @currentSid IS NOT NULL AND @currentSid <> @expectedSid
BEGIN
    SET @sql = N'DROP USER ' + QUOTENAME(@userName) + N';';
    EXEC sp_executesql @sql;
END;

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @userName)
BEGIN
    SET @sql = N'CREATE USER ' + QUOTENAME(@userName) + N' WITH SID = $sid, TYPE = E;';
    EXEC sp_executesql @sql;
END;

IF IS_ROLEMEMBER(N'db_datareader', @userName) <> 1
BEGIN
    SET @sql = N'ALTER ROLE db_datareader ADD MEMBER ' + QUOTENAME(@userName) + N';';
    EXEC sp_executesql @sql;
END;

IF IS_ROLEMEMBER(N'db_datawriter', @userName) <> 1
BEGIN
    SET @sql = N'ALTER ROLE db_datawriter ADD MEMBER ' + QUOTENAME(@userName) + N';';
    EXEC sp_executesql @sql;
END;

-- Temporary while DatabaseSchemaInitializer still mutates schema at API startup.
-- Remove this role together with WOR-136 when migrations move to deployment.
IF IS_ROLEMEMBER(N'db_ddladmin', @userName) <> 1
BEGIN
    SET @sql = N'ALTER ROLE db_ddladmin ADD MEMBER ' + QUOTENAME(@userName) + N';';
    EXEC sp_executesql @sql;
END;
"@

    [System.IO.File]::WriteAllText(
        $sqlFile.FullName,
        $sql,
        [System.Text.UTF8Encoding]::new($false)
    )

    $env:SQLCMDPASSWORD = $SqlAdminPassword
    & $sqlcmdCommand.Source `
        -S $sqlServerFqdn `
        -d $sqlDatabaseName `
        -U rbj `
        -b `
        -l 30 `
        -N `
        -i $sqlFile.FullName

    if ($LASTEXITCODE -ne 0) {
        throw 'SQL access provisioning failed.'
    }

    $sqlProvisioningSucceeded = $true
    Write-Host "SQL access provisioned for managed identity '$identityName'." -ForegroundColor Green
}
finally {
    $env:SQLCMDPASSWORD = $previousSqlCmdPassword
    $SqlAdminPassword = $null
    Remove-Item $sqlFile.FullName -Force -ErrorAction SilentlyContinue

    if ($firewallRuleCreated) {
        $deleteResult = Invoke-AzureCli -Arguments @(
            'sql', 'server', 'firewall-rule', 'delete',
            '--resource-group', $resourceGroup,
            '--server', $sqlServerName,
            '--name', $firewallRuleName,
            '--yes',
            '--only-show-errors',
            '--output', 'none'
        ) -AllowFailure

        if ($deleteResult.ExitCode -ne 0) {
            $message = "Could not remove temporary SQL provisioning firewall rule '$firewallRuleName'.`n$($deleteResult.Output)"
            if ($sqlProvisioningSucceeded) {
                throw $message
            }

            Write-Warning $message
        }
    }
}
